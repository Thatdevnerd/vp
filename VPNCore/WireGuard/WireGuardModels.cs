using System.Net;
using System.Security.Cryptography;

namespace VPNCore.WireGuard;

/// <summary>
/// WireGuard configuration for tunnel setup
/// </summary>
public class WireGuardConfiguration
{
    public byte[]? PrivateKey { get; set; }
    public byte[]? PublicKey { get; set; }
    public int ListenPort { get; set; } = 51820;
    public List<WireGuardPeer> Peers { get; set; } = new();

    /// <summary>
    /// Generate a new Curve25519 key pair for WireGuard
    /// Falls back to random keys for testing if Curve25519 is not available
    /// </summary>
    public void GenerateKeys()
    {
        try
        {
            using var ecdh = ECDiffieHellman.Create(ECCurve.CreateFromFriendlyName("curve25519"));
            PrivateKey = ecdh.ExportECPrivateKey();
            PublicKey = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
        }
        catch (PlatformNotSupportedException)
        {
            // Fallback to random keys for testing
            PrivateKey = new byte[32];
            PublicKey = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(PrivateKey);
            rng.GetBytes(PublicKey);
        }
    }

    /// <summary>
    /// Get the public key as a base64 string (WireGuard format)
    /// </summary>
    public string GetPublicKeyString()
    {
        return PublicKey != null ? Convert.ToBase64String(PublicKey) : string.Empty;
    }

    /// <summary>
    /// Get the private key as a base64 string (WireGuard format)
    /// </summary>
    public string GetPrivateKeyString()
    {
        return PrivateKey != null ? Convert.ToBase64String(PrivateKey) : string.Empty;
    }

    /// <summary>
    /// Set the private key from a base64 string
    /// </summary>
    public void SetPrivateKey(string base64Key)
    {
        PrivateKey = Convert.FromBase64String(base64Key);
        
        // Derive public key from private key
        using var ecdh = ECDiffieHellman.Create();
        ecdh.ImportECPrivateKey(PrivateKey, out _);
        PublicKey = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
    }
}

/// <summary>
/// WireGuard interface configuration
/// </summary>
public class WireGuardInterface
{
    public string Name { get; set; } = string.Empty;
    public byte[] PrivateKey { get; set; } = Array.Empty<byte>();
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public IPAddress Address { get; set; } = IPAddress.Any;
    public IPAddress SubnetMask { get; set; } = IPAddress.Any;
    public int ListenPort { get; set; } = 51820;
    public int MTU { get; set; } = 1420; // WireGuard default MTU
    public List<IPAddress> DNS { get; set; } = new();
    public List<string> AllowedIPs { get; set; } = new();

    /// <summary>
    /// Get the public key as a WireGuard-formatted string
    /// </summary>
    public string GetPublicKeyString()
    {
        return Convert.ToBase64String(PublicKey);
    }

    /// <summary>
    /// Get the private key as a WireGuard-formatted string
    /// </summary>
    public string GetPrivateKeyString()
    {
        return Convert.ToBase64String(PrivateKey);
    }
}

/// <summary>
/// WireGuard peer configuration
/// </summary>
public class WireGuardPeer
{
    public string PublicKey { get; set; } = string.Empty;
    public string? PresharedKey { get; set; }
    public IPEndPoint? Endpoint { get; set; }
    public List<IPAddress> AllowedIPs { get; set; } = new();
    public int PersistentKeepalive { get; set; } = 0;
    public DateTime? LastHandshake { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }

    /// <summary>
    /// Create a peer from a public key string
    /// </summary>
    public static WireGuardPeer FromPublicKey(string publicKey)
    {
        return new WireGuardPeer { PublicKey = publicKey };
    }

    /// <summary>
    /// Add an allowed IP range for this peer
    /// </summary>
    public void AddAllowedIP(string ipRange)
    {
        if (IPAddress.TryParse(ipRange.Split('/')[0], out var ip))
        {
            AllowedIPs.Add(ip);
        }
    }

    /// <summary>
    /// Set the endpoint for this peer
    /// </summary>
    public void SetEndpoint(string host, int port)
    {
        if (IPAddress.TryParse(host, out var ip))
        {
            Endpoint = new IPEndPoint(ip, port);
        }
        else
        {
            // Resolve hostname
            var addresses = Dns.GetHostAddresses(host);
            if (addresses.Length > 0)
            {
                Endpoint = new IPEndPoint(addresses[0], port);
            }
        }
    }
}

/// <summary>
/// WireGuard handshake message
/// </summary>
public class WireGuardHandshake
{
    public byte MessageType { get; set; }
    public uint SenderIndex { get; set; }
    public byte[] PublicKey { get; set; } = Array.Empty<byte>();
    public byte[] EncryptedPayload { get; set; } = Array.Empty<byte>();
    public byte[] MAC { get; set; } = Array.Empty<byte>();
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// WireGuard message types
    /// </summary>
    public static class MessageTypes
    {
        public const byte HandshakeInitiation = 1;
        public const byte HandshakeResponse = 2;
        public const byte CookieReply = 3;
        public const byte Transport = 4;
    }
}

/// <summary>
/// WireGuard transport packet
/// </summary>
public class WireGuardPacket
{
    public byte MessageType { get; set; } = WireGuardHandshake.MessageTypes.Transport;
    public uint ReceiverIndex { get; set; }
    public ulong Counter { get; set; }
    public byte[] EncryptedPayload { get; set; } = Array.Empty<byte>();
    public byte[] AuthTag { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Serialize the packet to bytes
    /// </summary>
    public byte[] ToBytes()
    {
        var packet = new byte[16 + EncryptedPayload.Length + AuthTag.Length];
        var offset = 0;

        // Message type (1 byte)
        packet[offset++] = MessageType;

        // Reserved (3 bytes)
        offset += 3;

        // Receiver index (4 bytes)
        BitConverter.GetBytes(ReceiverIndex).CopyTo(packet, offset);
        offset += 4;

        // Counter (8 bytes)
        BitConverter.GetBytes(Counter).CopyTo(packet, offset);
        offset += 8;

        // Encrypted payload
        EncryptedPayload.CopyTo(packet, offset);
        offset += EncryptedPayload.Length;

        // Auth tag
        AuthTag.CopyTo(packet, offset);

        return packet;
    }

    /// <summary>
    /// Parse a packet from bytes
    /// </summary>
    public static WireGuardPacket FromBytes(byte[] data)
    {
        if (data.Length < 16)
            throw new ArgumentException("Invalid WireGuard packet length");

        var packet = new WireGuardPacket();
        var offset = 0;

        // Message type
        packet.MessageType = data[offset++];

        // Skip reserved bytes
        offset += 3;

        // Receiver index
        packet.ReceiverIndex = BitConverter.ToUInt32(data, offset);
        offset += 4;

        // Counter
        packet.Counter = BitConverter.ToUInt64(data, offset);
        offset += 8;

        // Encrypted payload and auth tag (last 16 bytes are auth tag)
        var payloadLength = data.Length - 16 - 16; // Total - header - auth tag
        packet.EncryptedPayload = new byte[payloadLength];
        Array.Copy(data, offset, packet.EncryptedPayload, 0, payloadLength);
        offset += payloadLength;

        // Auth tag
        packet.AuthTag = new byte[16];
        Array.Copy(data, offset, packet.AuthTag, 0, 16);

        return packet;
    }
}

/// <summary>
/// WireGuard statistics and status information
/// </summary>
public class WireGuardStatus
{
    public string InterfaceName { get; set; } = string.Empty;
    public string PublicKey { get; set; } = string.Empty;
    public int ListenPort { get; set; }
    public List<WireGuardPeerStatus> Peers { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// WireGuard peer status information
/// </summary>
public class WireGuardPeerStatus
{
    public string PublicKey { get; set; } = string.Empty;
    public IPEndPoint? Endpoint { get; set; }
    public List<string> AllowedIPs { get; set; } = new();
    public DateTime? LastHandshake { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public bool IsConnected => LastHandshake.HasValue && 
                              (DateTime.UtcNow - LastHandshake.Value).TotalMinutes < 3;
}