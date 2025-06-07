using System.Net;
using VPNCore.Models;

namespace VPNCore.Networking;

public interface IVPNTransport : IDisposable
{
    event EventHandler<VPNPacketReceivedEventArgs>? PacketReceived;
    event EventHandler<VPNConnectionEventArgs>? ConnectionStateChanged;
    
    Task StartAsync(IPEndPoint localEndPoint, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task SendPacketAsync(VPNPacket packet, IPEndPoint destination, CancellationToken cancellationToken = default);
    bool IsRunning { get; }
    TransportProtocol Protocol { get; }
}

public enum TransportProtocol
{
    UDP,
    TCP
}

public class VPNPacketReceivedEventArgs : EventArgs
{
    public VPNPacket Packet { get; }
    public IPEndPoint Source { get; }

    public VPNPacketReceivedEventArgs(VPNPacket packet, IPEndPoint source)
    {
        Packet = packet;
        Source = source;
    }
}

public class VPNConnectionEventArgs : EventArgs
{
    public IPEndPoint EndPoint { get; }
    public ConnectionState State { get; }
    public string? Message { get; }

    public VPNConnectionEventArgs(IPEndPoint endPoint, ConnectionState state, string? message = null)
    {
        EndPoint = endPoint;
        State = state;
        Message = message;
    }
}

public enum ConnectionState
{
    Connected,
    Disconnected,
    Error
}