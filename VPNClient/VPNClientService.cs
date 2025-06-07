using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VPNCore.Interfaces;
using VPNCore.Models;
using VPNCore.Networking;
using VPNCore.WireGuard;

namespace VPNClient;

public class VPNClientService : IVPNClient, IDisposable
{
    private readonly ILogger<VPNClientService> _logger;
    private readonly IVPNCryptography _cryptography;
    private readonly IVPNTunnel _tunnel;
    private UdpClient? _udpClient;
    private VPNProtocol? _protocol;
    private VPNClientInfo _clientInfo;
    private VPNConnectionStatus _status = VPNConnectionStatus.Disconnected;
    private CancellationTokenSource? _cancellationTokenSource;
    private IPEndPoint? _serverEndPoint;
    private Timer? _heartbeatTimer;
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);
    private DateTime _startTime = DateTime.UtcNow;

    public VPNClientService(
        ILogger<VPNClientService> logger,
        IVPNCryptography cryptography,
        IVPNTunnel tunnel)
    {
        _logger = logger;
        _cryptography = cryptography;
        _tunnel = tunnel;
        _clientInfo = new VPNClientInfo();
    }

    public event EventHandler<VPNConnectionStatus>? StatusChanged;
    public event EventHandler<string>? LogMessage;

    public async Task ConnectAsync(VPNConfiguration configuration, CancellationToken cancellationToken = default)
    {
        try
        {
            SetStatus(VPNConnectionStatus.Connecting);
            _logger.LogInformation("Connecting to VPN server {Server}:{Port}", 
                configuration.ServerAddress, configuration.ServerPort);

            _serverEndPoint = new IPEndPoint(IPAddress.Parse(configuration.ServerAddress), configuration.ServerPort);
            _udpClient = new UdpClient();
            _protocol = new VPNProtocol(_cryptography, _logger);
            _protocol.SetUdpClient(_udpClient);

            _cancellationTokenSource = new CancellationTokenSource();

            // Send handshake
            await SendHandshakeAsync();

            // Wait for server response
            var response = await ReceivePacketAsync(cancellationToken);
            if (response?.Type == VPNPacketType.KeyExchange)
            {
                await HandleKeyExchangeResponse(response);
                SetStatus(VPNConnectionStatus.Connected);
                
                // Start packet processing
                _ = Task.Run(() => ProcessPacketsAsync(_cancellationTokenSource.Token), cancellationToken);
                
                // Start heartbeat timer
                _heartbeatTimer = new Timer(SendHeartbeat, null, _heartbeatInterval, _heartbeatInterval);
                
                _logger.LogInformation("Successfully connected to VPN server");
                LogMessage?.Invoke(this, "Connected to VPN server");
            }
            else
            {
                throw new InvalidOperationException("Invalid server response");
            }
        }
        catch (Exception ex)
        {
            SetStatus(VPNConnectionStatus.Error);
            _logger.LogError(ex, "Failed to connect to VPN server");
            throw;
        }
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (_status == VPNConnectionStatus.Connected && _serverEndPoint != null)
            {
                var disconnectPacket = new VPNPacket
                {
                    Type = VPNPacketType.Disconnect,
                    SessionId = _clientInfo.ClientId
                };

                await SendPacketAsync(disconnectPacket);
            }

            _cancellationTokenSource?.Cancel();
            _heartbeatTimer?.Dispose();
            
            if (_tunnel.IsActive)
            {
                await _tunnel.DestroyTunnelAsync();
            }

            _udpClient?.Close();
            _udpClient?.Dispose();
            _protocol?.Dispose();

            SetStatus(VPNConnectionStatus.Disconnected);
            _logger.LogInformation("Disconnected from VPN server");
            LogMessage?.Invoke(this, "Disconnected from VPN server");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");
        }
    }

    public Task<VPNConnectionStatus> GetStatusAsync()
    {
        return Task.FromResult(_status);
    }

    public Task<VPNClientInfo> GetClientInfoAsync()
    {
        return Task.FromResult(_clientInfo);
    }

    private async Task SendHandshakeAsync()
    {
        var (publicKey, privateKey) = _cryptography.GenerateKeyPair();
        
        var handshakePacket = new VPNPacket
        {
            Type = VPNPacketType.Handshake,
            SessionId = _clientInfo.ClientId,
            Data = System.Text.Encoding.UTF8.GetBytes($"CLIENT_VERSION:1.0.0;PUBLIC_KEY:{Convert.ToBase64String(publicKey)}")
        };

        await SendPacketAsync(handshakePacket);
    }

    private async Task HandleKeyExchangeResponse(VPNPacket response)
    {
        var responseData = System.Text.Encoding.UTF8.GetString(response.Data);
        if (responseData.StartsWith("ASSIGNED_IP:"))
        {
            var assignedIpStr = responseData.Substring("ASSIGNED_IP:".Length);
            if (IPAddress.TryParse(assignedIpStr, out var assignedIP))
            {
                _clientInfo.AssignedIP = assignedIP;
                _clientInfo.ClientId = response.SessionId;
                
                // Create tunnel interface
                await _tunnel.CreateTunnelAsync("vpn0", assignedIP, IPAddress.Parse("255.255.255.0"));
                
                _logger.LogInformation("Assigned virtual IP: {IP}", assignedIP);
            }
        }
    }

    private async Task ProcessPacketsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _status == VPNConnectionStatus.Connected)
        {
            try
            {
                // Process incoming packets from server
                var packet = await ReceivePacketAsync(cancellationToken);
                if (packet != null)
                {
                    await HandleIncomingPacket(packet);
                }

                // Process outgoing packets from tunnel
                if (_tunnel.IsActive)
                {
                    try
                    {
                        var tunnelData = await _tunnel.ReadPacketAsync(cancellationToken);
                        if (tunnelData.Length > 0)
                        {
                            var dataPacket = new VPNPacket
                            {
                                Type = VPNPacketType.Data,
                                SessionId = _clientInfo.ClientId,
                                Data = tunnelData
                            };

                            await SendPacketAsync(dataPacket);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Expected when no data is available
                    }
                }

                await Task.Delay(1, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing packets");
            }
        }
    }

    private async Task HandleIncomingPacket(VPNPacket packet)
    {
        switch (packet.Type)
        {
            case VPNPacketType.Data:
                if (_tunnel.IsActive)
                {
                    await _tunnel.WritePacketAsync(packet.Data);
                    _clientInfo.BytesReceived += packet.Data.Length;
                }
                break;

            case VPNPacketType.KeepAlive:
                _clientInfo.LastActivity = DateTime.UtcNow;
                // Send keep-alive response
                var response = new VPNPacket
                {
                    Type = VPNPacketType.KeepAlive,
                    SessionId = _clientInfo.ClientId
                };
                await SendPacketAsync(response);
                break;

            case VPNPacketType.Disconnect:
                SetStatus(VPNConnectionStatus.Disconnected);
                break;

            case VPNPacketType.Heartbeat:
                await HandleServerHeartbeatAsync(packet);
                break;

            case VPNPacketType.HeartbeatResponse:
                await HandleHeartbeatResponseAsync(packet);
                break;
        }
    }

    private async Task<VPNPacket?> ReceivePacketAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_udpClient == null) return null;

            var result = await _udpClient.ReceiveAsync();
            return DeserializePacket(result.Buffer);
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error receiving packet");
            return null;
        }
    }

    private async Task SendPacketAsync(VPNPacket packet)
    {
        try
        {
            if (_udpClient == null || _serverEndPoint == null) return;

            var data = SerializePacket(packet);
            await _udpClient.SendAsync(data, _serverEndPoint);
            _clientInfo.BytesSent += data.Length;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending packet");
        }
    }

    private VPNPacket? DeserializePacket(byte[] data)
    {
        try
        {
            if (data.Length < 8) return null;

            var type = (VPNPacketType)data[0];
            var sessionIdLength = BitConverter.ToInt32(data, 1);
            if (data.Length < 5 + sessionIdLength) return null;

            var sessionId = System.Text.Encoding.UTF8.GetString(data, 5, sessionIdLength);
            var payloadLength = BitConverter.ToInt32(data, 5 + sessionIdLength);
            var payload = new byte[payloadLength];
            Array.Copy(data, 9 + sessionIdLength, payload, 0, payloadLength);

            return new VPNPacket
            {
                Type = type,
                SessionId = sessionId,
                Data = payload,
                Timestamp = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }

    private byte[] SerializePacket(VPNPacket packet)
    {
        var sessionIdBytes = System.Text.Encoding.UTF8.GetBytes(packet.SessionId);
        var result = new byte[9 + sessionIdBytes.Length + packet.Data.Length];
        
        result[0] = (byte)packet.Type;
        BitConverter.GetBytes(sessionIdBytes.Length).CopyTo(result, 1);
        sessionIdBytes.CopyTo(result, 5);
        BitConverter.GetBytes(packet.Data.Length).CopyTo(result, 5 + sessionIdBytes.Length);
        packet.Data.CopyTo(result, 9 + sessionIdBytes.Length);
        
        return result;
    }

    private void SetStatus(VPNConnectionStatus status)
    {
        if (_status != status)
        {
            _status = status;
            _clientInfo.Status = status;
            StatusChanged?.Invoke(this, status);
        }
    }

    private async void SendHeartbeat(object? state)
    {
        try
        {
            if (_status != VPNConnectionStatus.Connected) return;

            var heartbeat = new VPNHeartbeatPacket
            {
                Type = VPNPacketType.Heartbeat,
                SessionId = _clientInfo.ClientId,
                ClientTime = DateTime.UtcNow,
                ClientUptime = (long)(DateTime.UtcNow - _startTime).TotalSeconds,
                PendingPackets = 0, // Could track actual pending packets
                ClientStatus = "Active"
            };

            var packet = new VPNPacket
            {
                Type = VPNPacketType.Heartbeat,
                SessionId = _clientInfo.ClientId,
                Data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(heartbeat),
                Timestamp = DateTime.UtcNow
            };

            await SendPacketAsync(packet);
            _logger.LogDebug("Sent heartbeat to server");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeat");
        }
    }

    private async Task HandleServerHeartbeatAsync(VPNPacket packet)
    {
        try
        {
            // Server is checking if we're alive - respond immediately
            var response = new VPNHeartbeatResponsePacket
            {
                Type = VPNPacketType.HeartbeatResponse,
                SessionId = _clientInfo.ClientId,
                ServerTime = DateTime.UtcNow,
                ClientTime = DateTime.UtcNow,
                RoundTripTime = 0, // Will be calculated by server
                ServerStatus = "Active",
                ConnectedClients = 1
            };

            var responsePacket = new VPNPacket
            {
                Type = VPNPacketType.HeartbeatResponse,
                SessionId = _clientInfo.ClientId,
                Data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(response),
                Timestamp = DateTime.UtcNow
            };

            await SendPacketAsync(responsePacket);
            _clientInfo.LastActivity = DateTime.UtcNow;
            _clientInfo.LastHeartbeat = DateTime.UtcNow;
            
            _logger.LogDebug("Responded to server heartbeat");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling server heartbeat");
        }
    }

    private async Task HandleHeartbeatResponseAsync(VPNPacket packet)
    {
        try
        {
            var response = System.Text.Json.JsonSerializer.Deserialize<VPNHeartbeatResponsePacket>(packet.Data);
            if (response != null)
            {
                // Calculate round trip time
                var rtt = (DateTime.UtcNow - response.ClientTime).TotalMilliseconds;
                
                if (_clientInfo.AverageRoundTripTime == 0)
                {
                    _clientInfo.AverageRoundTripTime = (long)rtt;
                }
                else
                {
                    // Exponential moving average
                    _clientInfo.AverageRoundTripTime = (long)(0.8 * _clientInfo.AverageRoundTripTime + 0.2 * rtt);
                }

                _clientInfo.LastActivity = DateTime.UtcNow;
                _clientInfo.LastHeartbeat = DateTime.UtcNow;
                _clientInfo.MissedHeartbeats = 0;

                _logger.LogDebug("Received heartbeat response from server, RTT: {RTT:F2}ms", rtt);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing heartbeat response");
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource?.Dispose();
        _heartbeatTimer?.Dispose();
        _udpClient?.Dispose();
        _protocol?.Dispose();
        _tunnel?.Dispose();
        GC.SuppressFinalize(this);
    }
}