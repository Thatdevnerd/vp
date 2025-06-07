using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VPNCore.Models;

namespace VPNCore.Networking;

public class UDPVPNTransport : IVPNTransport
{
    private readonly ILogger<UDPVPNTransport> _logger;
    private UdpClient? _udpClient;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _receiveTask;
    private bool _disposed;

    public event EventHandler<VPNPacketReceivedEventArgs>? PacketReceived;
    public event EventHandler<VPNConnectionEventArgs>? ConnectionStateChanged;

    public bool IsRunning { get; private set; }
    public TransportProtocol Protocol => TransportProtocol.UDP;

    public UDPVPNTransport(ILogger<UDPVPNTransport> logger)
    {
        _logger = logger;
    }

    public async Task StartAsync(IPEndPoint localEndPoint, CancellationToken cancellationToken = default)
    {
        if (IsRunning)
            throw new InvalidOperationException("Transport is already running");

        try
        {
            _udpClient = new UdpClient(localEndPoint);
            _cancellationTokenSource = new CancellationTokenSource();
            
            _receiveTask = ReceivePacketsAsync(_cancellationTokenSource.Token);
            IsRunning = true;
            
            _logger.LogInformation("UDP VPN transport started on {EndPoint}", localEndPoint);
            ConnectionStateChanged?.Invoke(this, new VPNConnectionEventArgs(localEndPoint, ConnectionState.Connected));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start UDP VPN transport");
            ConnectionStateChanged?.Invoke(this, new VPNConnectionEventArgs(localEndPoint, ConnectionState.Error, ex.Message));
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRunning)
            return;

        try
        {
            _cancellationTokenSource?.Cancel();
            
            if (_receiveTask != null)
                await _receiveTask;

            _udpClient?.Close();
            _udpClient?.Dispose();
            _udpClient = null;

            IsRunning = false;
            _logger.LogInformation("UDP VPN transport stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping UDP VPN transport");
        }
    }

    public async Task SendPacketAsync(VPNPacket packet, IPEndPoint destination, CancellationToken cancellationToken = default)
    {
        if (!IsRunning || _udpClient == null)
            throw new InvalidOperationException("Transport is not running");

        try
        {
            var serializedPacket = SerializePacket(packet);
            await _udpClient.SendAsync(serializedPacket, destination, cancellationToken);
            
            _logger.LogDebug("Sent {PacketType} packet to {Destination}, size: {Size} bytes", 
                packet.Type, destination, serializedPacket.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send packet to {Destination}", destination);
            throw;
        }
    }

    private async Task ReceivePacketsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _udpClient != null)
        {
            try
            {
                var result = await _udpClient.ReceiveAsync(cancellationToken);
                var packet = DeserializePacket(result.Buffer);
                
                if (packet != null)
                {
                    _logger.LogDebug("Received {PacketType} packet from {Source}, size: {Size} bytes",
                        packet.Type, result.RemoteEndPoint, result.Buffer.Length);
                    
                    PacketReceived?.Invoke(this, new VPNPacketReceivedEventArgs(packet, result.RemoteEndPoint));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving UDP packet");
            }
        }
    }

    private static byte[] SerializePacket(VPNPacket packet)
    {
        var json = JsonSerializer.Serialize(packet, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return System.Text.Encoding.UTF8.GetBytes(json);
    }

    private static VPNPacket? DeserializePacket(byte[] data)
    {
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(data);
            return JsonSerializer.Deserialize<VPNPacket>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        StopAsync().Wait(TimeSpan.FromSeconds(5));
        _cancellationTokenSource?.Dispose();
        _disposed = true;
    }
}