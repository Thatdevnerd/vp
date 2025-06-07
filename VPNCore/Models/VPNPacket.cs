namespace VPNCore.Models;

public class VPNPacket
{
    public VPNPacketType Type { get; set; }
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public uint SequenceNumber { get; set; }
    public string SessionId { get; set; } = string.Empty;
    public bool IsEncrypted { get; set; }
    public byte[] IV { get; set; } = Array.Empty<byte>();
    public byte[] AuthTag { get; set; } = Array.Empty<byte>();
}

public enum VPNPacketType : byte
{
    Handshake = 0x01,
    KeyExchange = 0x02,
    Data = 0x03,
    KeepAlive = 0x04,
    Disconnect = 0x05,
    Acknowledgment = 0x06,
    Error = 0x07,
    Configuration = 0x08,
    Heartbeat = 0x09,
    HeartbeatResponse = 0x0A
}

public class VPNHandshakePacket : VPNPacket
{
    public string ClientVersion { get; set; } = string.Empty;
    public string[] SupportedCiphers { get; set; } = Array.Empty<string>();
    public byte[] ClientPublicKey { get; set; } = Array.Empty<byte>();
    public byte[] ClientCertificate { get; set; } = Array.Empty<byte>();
}

public class VPNKeyExchangePacket : VPNPacket
{
    public byte[] ServerPublicKey { get; set; } = Array.Empty<byte>();
    public byte[] ServerCertificate { get; set; } = Array.Empty<byte>();
    public byte[] SharedSecret { get; set; } = Array.Empty<byte>();
    public string SelectedCipher { get; set; } = string.Empty;
}

public class VPNDataPacket : VPNPacket
{
    public byte[] OriginalData { get; set; } = Array.Empty<byte>();
    public bool IsCompressed { get; set; }
    public int OriginalLength { get; set; }
}

public class VPNHeartbeatPacket : VPNPacket
{
    public DateTime ClientTime { get; set; } = DateTime.UtcNow;
    public long ClientUptime { get; set; }
    public int PendingPackets { get; set; }
    public string ClientStatus { get; set; } = "Active";
}

public class VPNHeartbeatResponsePacket : VPNPacket
{
    public DateTime ServerTime { get; set; } = DateTime.UtcNow;
    public DateTime ClientTime { get; set; }
    public long RoundTripTime { get; set; }
    public string ServerStatus { get; set; } = "Active";
    public int ConnectedClients { get; set; }
}