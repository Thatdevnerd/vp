using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VPNCore.Interfaces;
using VPNCore.Models;
using VPNCore.Networking;
using VPNCore.Services;
using VPNCore.WireGuard;

namespace VPNServer;

public class VPNServerService : BackgroundService, IVPNServer
{
    private readonly ILogger<VPNServerService> _logger;
    private readonly VPNConfiguration _configuration;
    private readonly IVPNCryptography _cryptography;
    private readonly ConcurrentDictionary<string, VPNClientInfo> _clients = new();
    private readonly ConcurrentDictionary<IPEndPoint, string> _endpointToClientId = new();
    private readonly IPAddressPool _ipPool;
    private UdpClient? _udpServer;
    private VPNProtocol? _protocol;
    private VPNClientHealthMonitor? _healthMonitor;
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly VPNServerInfo _serverInfo;
    private readonly WireGuardConfiguration _wireGuardConfig;
    private readonly ConcurrentDictionary<string, WireGuardVPNTunnel> _clientTunnels = new();

    public VPNServerService(
        ILogger<VPNServerService> logger,
        VPNConfiguration configuration,
        IVPNCryptography cryptography)
    {
        _logger = logger;
        _configuration = configuration;
        _cryptography = cryptography;
        _ipPool = new IPAddressPool(_configuration.VirtualNetworkAddress, _configuration.VirtualNetworkMask);
        _wireGuardConfig = new WireGuardConfiguration();
        _wireGuardConfig.GenerateKeys(); // Generate server keys
        
        _serverInfo = new VPNServerInfo
        {
            ServerIP = IPAddress.Any,
            Port = _configuration.ServerPort,
            MaxClients = 100
        };
        
        _logger.LogInformation($"VPN Server initialized with WireGuard support. Public key: {_wireGuardConfig.GetPublicKeyString()}");
    }

    public event EventHandler<VPNClientInfo>? ClientConnected;
    public event EventHandler<VPNClientInfo>? ClientDisconnected;
    public event EventHandler<string>? LogMessage;

    public new async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_isRunning) return;

        try
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _udpServer = new UdpClient(_configuration.ServerPort);
            _protocol = new VPNProtocol(_cryptography, _logger);
            _protocol.SetUdpClient(_udpServer);

            // Initialize health monitor
            var transportLogger = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole())
                .CreateLogger<UDPVPNTransport>();
            var transport = new UDPVPNTransport(transportLogger);
            
            var healthMonitorLogger = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole())
                .CreateLogger<VPNClientHealthMonitor>();
            _healthMonitor = new VPNClientHealthMonitor(healthMonitorLogger, transport);
            _healthMonitor.ClientTimedOut += OnClientTimedOut;
            _healthMonitor.ClientHealthChanged += OnClientHealthChanged;

            _isRunning = true;
            _serverInfo.StartTime = DateTime.UtcNow;

            _logger.LogInformation("VPN Server started on port {Port}", _configuration.ServerPort);
            LogMessage?.Invoke(this, $"VPN Server started on port {_configuration.ServerPort}");

            // Start packet processing loop
            _ = Task.Run(() => ProcessPacketsAsync(_cancellationTokenSource.Token), cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start VPN server");
            throw;
        }
    }

    public new async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!_isRunning) return;

        try
        {
            _isRunning = false;
            _cancellationTokenSource?.Cancel();

            // Disconnect all clients
            foreach (var client in _clients.Values)
            {
                ClientDisconnected?.Invoke(this, client);
            }
            _clients.Clear();
            _endpointToClientId.Clear();

            _udpServer?.Close();
            _udpServer?.Dispose();
            _protocol?.Dispose();
            _healthMonitor?.Dispose();

            _logger.LogInformation("VPN Server stopped");
            LogMessage?.Invoke(this, "VPN Server stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping VPN server");
        }
    }

    public Task<bool> IsRunningAsync()
    {
        return Task.FromResult(_isRunning);
    }

    public Task<VPNServerInfo> GetServerInfoAsync()
    {
        _serverInfo.ConnectedClients = _clients.Count;
        _serverInfo.Clients = _clients.Values.ToList();
        return Task.FromResult(_serverInfo);
    }

    public Task<IEnumerable<VPNClientInfo>> GetConnectedClientsAsync()
    {
        return Task.FromResult<IEnumerable<VPNClientInfo>>(_clients.Values);
    }

    public Task DisconnectClientAsync(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            _ipPool.ReleaseIP(client.AssignedIP);
            ClientDisconnected?.Invoke(this, client);
            _logger.LogInformation("Client {ClientId} disconnected", clientId);
        }
        return Task.CompletedTask;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await StartAsync(stoppingToken);

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        finally
        {
            await StopAsync(stoppingToken);
        }
    }

    private async Task ProcessPacketsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _isRunning)
        {
            try
            {
                if (_udpServer == null || _protocol == null) break;

                var result = await _udpServer.ReceiveAsync();
                var packet = DeserializePacket(result.Buffer);
                
                if (packet != null)
                {
                    await HandlePacketAsync(packet, result.RemoteEndPoint);
                }
            }
            catch (ObjectDisposedException)
            {
                // UDP client was disposed, exit loop
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing packet");
            }
        }
    }

    private VPNPacket? DeserializePacket(byte[] data)
    {
        try
        {
            // Simple packet deserialization - in production use proper protocol
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

    private async Task HandlePacketAsync(VPNPacket packet, IPEndPoint remoteEndPoint)
    {
        switch (packet.Type)
        {
            case VPNPacketType.Handshake:
                await HandleHandshakeAsync(packet, remoteEndPoint);
                break;
            case VPNPacketType.Data:
                await HandleDataPacketAsync(packet, remoteEndPoint);
                break;
            case VPNPacketType.KeepAlive:
                await HandleKeepAliveAsync(packet, remoteEndPoint);
                break;
            case VPNPacketType.Disconnect:
                await HandleDisconnectAsync(packet, remoteEndPoint);
                break;
            case VPNPacketType.Heartbeat:
                await HandleHeartbeatAsync(packet, remoteEndPoint);
                break;
            case VPNPacketType.HeartbeatResponse:
                await HandleHeartbeatResponseAsync(packet, remoteEndPoint);
                break;
            default:
                _logger.LogWarning("Unknown packet type {Type} from {EndPoint}", packet.Type, remoteEndPoint);
                break;
        }
    }

    private async Task HandleHandshakeAsync(VPNPacket packet, IPEndPoint remoteEndPoint)
    {
        try
        {
            var clientId = Guid.NewGuid().ToString();
            var assignedIP = _ipPool.AssignIP();

            var client = new VPNClientInfo
            {
                ClientId = clientId,
                AssignedIP = assignedIP,
                PublicIP = remoteEndPoint.Address,
                Status = VPNConnectionStatus.Connected,
                SessionKey = _cryptography.GenerateRandomBytes(32)
            };

            _clients[clientId] = client;
            _endpointToClientId[remoteEndPoint] = clientId;

            // Register client with health monitor
            _healthMonitor?.RegisterClient(client);

            // Send handshake response
            var response = new VPNPacket
            {
                Type = VPNPacketType.KeyExchange,
                SessionId = clientId,
                Data = System.Text.Encoding.UTF8.GetBytes($"ASSIGNED_IP:{assignedIP}")
            };

            await SendPacketAsync(response, remoteEndPoint);

            ClientConnected?.Invoke(this, client);
            _logger.LogInformation("Client {ClientId} connected from {EndPoint} with IP {AssignedIP}", 
                clientId, remoteEndPoint, assignedIP);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling handshake from {EndPoint}", remoteEndPoint);
        }
    }

    private async Task HandleDataPacketAsync(VPNPacket packet, IPEndPoint remoteEndPoint)
    {
        if (_endpointToClientId.TryGetValue(remoteEndPoint, out var clientId) &&
            _clients.TryGetValue(clientId, out var client))
        {
            client.LastActivity = DateTime.UtcNow;
            client.BytesReceived += packet.Data.Length;

            // In a real implementation, this would route the packet through the TUN/TAP interface
            _logger.LogDebug("Received data packet from {ClientId}, size: {Size} bytes", 
                clientId, packet.Data.Length);

            // Send acknowledgment
            var ack = new VPNPacket
            {
                Type = VPNPacketType.Acknowledgment,
                SessionId = clientId,
                SequenceNumber = packet.SequenceNumber
            };

            await SendPacketAsync(ack, remoteEndPoint);
        }
    }

    private async Task HandleKeepAliveAsync(VPNPacket packet, IPEndPoint remoteEndPoint)
    {
        if (_endpointToClientId.TryGetValue(remoteEndPoint, out var clientId) &&
            _clients.TryGetValue(clientId, out var client))
        {
            client.LastActivity = DateTime.UtcNow;

            var response = new VPNPacket
            {
                Type = VPNPacketType.KeepAlive,
                SessionId = clientId
            };

            await SendPacketAsync(response, remoteEndPoint);
        }
    }

    private Task HandleDisconnectAsync(VPNPacket packet, IPEndPoint remoteEndPoint)
    {
        if (_endpointToClientId.TryRemove(remoteEndPoint, out var clientId))
        {
            if (_clients.TryRemove(clientId, out var client))
            {
                _healthMonitor?.UnregisterClient(clientId);
                _ipPool.ReleaseIP(client.AssignedIP);
                ClientDisconnected?.Invoke(this, client);
                _logger.LogInformation("Client {ClientId} disconnected", clientId);
            }
        }
        return Task.CompletedTask;
    }

    private async Task HandleHeartbeatAsync(VPNPacket packet, IPEndPoint remoteEndPoint)
    {
        if (_endpointToClientId.TryGetValue(remoteEndPoint, out var clientId) &&
            _clients.TryGetValue(clientId, out var client))
        {
            try
            {
                // Deserialize heartbeat packet
                var heartbeatData = System.Text.Json.JsonSerializer.Deserialize<VPNHeartbeatPacket>(packet.Data);
                if (heartbeatData != null && _healthMonitor != null)
                {
                    heartbeatData.SessionId = clientId;
                    var response = await _healthMonitor.ProcessHeartbeatAsync(clientId, heartbeatData);
                    
                    // Send heartbeat response
                    var responsePacket = new VPNPacket
                    {
                        Type = VPNPacketType.HeartbeatResponse,
                        SessionId = clientId,
                        Data = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(response)
                    };

                    await SendPacketAsync(responsePacket, remoteEndPoint);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing heartbeat from client {ClientId}", clientId);
            }
        }
    }

    private Task HandleHeartbeatResponseAsync(VPNPacket packet, IPEndPoint remoteEndPoint)
    {
        // Client responding to server heartbeat - update last activity
        if (_endpointToClientId.TryGetValue(remoteEndPoint, out var clientId) &&
            _clients.TryGetValue(clientId, out var client))
        {
            client.LastActivity = DateTime.UtcNow;
            client.LastHeartbeat = DateTime.UtcNow;
            client.MissedHeartbeats = 0;
            
            _logger.LogDebug("Received heartbeat response from client {ClientId}", clientId);
        }
        return Task.CompletedTask;
    }

    private void OnClientTimedOut(object? sender, VPNClientInfo client)
    {
        _logger.LogWarning("Client {ClientId} timed out, removing from server", client.ClientId);
        
        // Find and remove the client's endpoint mapping
        var endpointToRemove = _endpointToClientId.FirstOrDefault(kvp => kvp.Value == client.ClientId).Key;
        if (endpointToRemove != null)
        {
            _endpointToClientId.TryRemove(endpointToRemove, out _);
        }
        
        // Remove client and release resources
        if (_clients.TryRemove(client.ClientId, out _))
        {
            _ipPool.ReleaseIP(client.AssignedIP);
            ClientDisconnected?.Invoke(this, client);
        }
    }

    private void OnClientHealthChanged(object? sender, VPNClientInfo client)
    {
        _logger.LogInformation("Client {ClientId} health status changed to {Status}", 
            client.ClientId, client.IsHealthy ? "healthy" : "unhealthy");
        
        if (!client.IsHealthy)
        {
            LogMessage?.Invoke(this, $"Client {client.ClientId} is experiencing connectivity issues");
        }
    }

    private async Task SendPacketAsync(VPNPacket packet, IPEndPoint remoteEndPoint)
    {
        try
        {
            var data = SerializePacket(packet);
            if (_udpServer != null)
            {
                await _udpServer.SendAsync(data, remoteEndPoint);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending packet to {EndPoint}", remoteEndPoint);
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
}

public class IPAddressPool
{
    private readonly IPAddress _networkAddress;
    private readonly IPAddress _subnetMask;
    private readonly HashSet<IPAddress> _assignedIPs = new();
    private readonly object _lock = new();

    public IPAddressPool(IPAddress networkAddress, IPAddress subnetMask)
    {
        _networkAddress = networkAddress;
        _subnetMask = subnetMask;
    }

    public IPAddress AssignIP()
    {
        lock (_lock)
        {
            var networkBytes = _networkAddress.GetAddressBytes();

            for (int i = 1; i < 254; i++)
            {
                var ipBytes = (byte[])networkBytes.Clone();
                ipBytes[3] = (byte)i;
                var ip = new IPAddress(ipBytes);

                if (!_assignedIPs.Contains(ip))
                {
                    _assignedIPs.Add(ip);
                    return ip;
                }
            }

            throw new InvalidOperationException("No available IP addresses in pool");
        }
    }

    public void ReleaseIP(IPAddress ip)
    {
        lock (_lock)
        {
            _assignedIPs.Remove(ip);
        }
    }
}