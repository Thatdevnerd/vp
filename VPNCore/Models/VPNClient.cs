using System;
using System.Collections.Generic;
using System.Net;

namespace VPNCore.Models;

public class VPNClientInfo
{
    public string ClientId { get; set; } = Guid.NewGuid().ToString();
    public IPAddress AssignedIP { get; set; } = IPAddress.None;
    public IPAddress PublicIP { get; set; } = IPAddress.None;
    public DateTime ConnectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public VPNConnectionStatus Status { get; set; } = VPNConnectionStatus.Disconnected;
    public string Username { get; set; } = string.Empty;
    public byte[] SessionKey { get; set; } = Array.Empty<byte>();
    public uint SequenceNumber { get; set; }
    public string ClientVersion { get; set; } = string.Empty;
    public long AverageRoundTripTime { get; set; }
    public int MissedHeartbeats { get; set; }
    public bool IsHealthy => (DateTime.UtcNow - LastHeartbeat).TotalSeconds < 60 && MissedHeartbeats < 3;
}

public enum VPNConnectionStatus
{
    Disconnected,
    Connecting,
    Authenticating,
    Connected,
    Reconnecting,
    Error
}

public class VPNServerInfo
{
    public string ServerId { get; set; } = Environment.MachineName;
    public IPAddress ServerIP { get; set; } = IPAddress.Any;
    public int Port { get; set; } = 1194;
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public int ConnectedClients { get; set; }
    public int MaxClients { get; set; } = 100;
    public long TotalBytesTransferred { get; set; }
    public string Version { get; set; } = "1.0.0";
    public List<VPNClientInfo> Clients { get; set; } = new();
}