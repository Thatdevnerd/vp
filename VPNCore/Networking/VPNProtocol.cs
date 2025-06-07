using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VPNCore.Interfaces;
using VPNCore.Models;

namespace VPNCore.Networking;

public class VPNProtocol : IDisposable
{
    private readonly IVPNCryptography _cryptography;
    private readonly ILogger _logger;
    private UdpClient? _udpClient;
    private TcpClient? _tcpClient;
    private readonly bool _useUdp;

    public VPNProtocol(IVPNCryptography cryptography, ILogger logger, bool useUdp = true)
    {
        _cryptography = cryptography;
        _logger = logger;
        _useUdp = useUdp;
    }

    public async Task<VPNPacket> ReceivePacketAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            byte[] data;
            
            if (_useUdp && _udpClient != null)
            {
                var result = await _udpClient.ReceiveAsync();
                data = result.Buffer;
            }
            else if (!_useUdp && _tcpClient?.GetStream() != null)
            {
                var stream = _tcpClient.GetStream();
                var lengthBuffer = new byte[4];
                await stream.ReadExactlyAsync(lengthBuffer, cancellationToken);
                var length = BitConverter.ToInt32(lengthBuffer, 0);
                
                data = new byte[length];
                await stream.ReadExactlyAsync(data, cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("No active connection");
            }

            return DeserializePacket(data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to receive VPN packet");
            throw;
        }
    }

    public async Task SendPacketAsync(VPNPacket packet, IPEndPoint? remoteEndPoint = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var data = SerializePacket(packet);

            if (_useUdp && _udpClient != null && remoteEndPoint != null)
            {
                await _udpClient.SendAsync(data, remoteEndPoint);
            }
            else if (!_useUdp && _tcpClient?.GetStream() != null)
            {
                var stream = _tcpClient.GetStream();
                var lengthBuffer = BitConverter.GetBytes(data.Length);
                await stream.WriteAsync(lengthBuffer, cancellationToken);
                await stream.WriteAsync(data, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            else
            {
                throw new InvalidOperationException("No active connection");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send VPN packet");
            throw;
        }
    }

    public void InitializeUdpClient(int port)
    {
        _udpClient = new UdpClient(port);
        _logger.LogInformation($"UDP client initialized on port {port}");
    }

    public void InitializeTcpClient(string host, int port)
    {
        _tcpClient = new TcpClient();
        _tcpClient.Connect(host, port);
        _logger.LogInformation($"TCP client connected to {host}:{port}");
    }

    public void SetUdpClient(UdpClient udpClient)
    {
        _udpClient = udpClient;
    }

    public void SetTcpClient(TcpClient tcpClient)
    {
        _tcpClient = tcpClient;
    }

    private byte[] SerializePacket(VPNPacket packet)
    {
        try
        {
            var json = JsonSerializer.Serialize(packet, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            
            var jsonBytes = Encoding.UTF8.GetBytes(json);
            var header = new byte[8];
            
            // Magic number (4 bytes)
            BitConverter.GetBytes(0x56504E50).CopyTo(header, 0); // "VPNP"
            
            // Packet length (4 bytes)
            BitConverter.GetBytes(jsonBytes.Length).CopyTo(header, 4);
            
            var result = new byte[header.Length + jsonBytes.Length];
            header.CopyTo(result, 0);
            jsonBytes.CopyTo(result, header.Length);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize VPN packet");
            throw;
        }
    }

    private VPNPacket DeserializePacket(byte[] data)
    {
        try
        {
            if (data.Length < 8)
                throw new ArgumentException("Invalid packet: too short");

            // Verify magic number
            var magicNumber = BitConverter.ToInt32(data, 0);
            if (magicNumber != 0x56504E50) // "VPNP"
                throw new ArgumentException("Invalid packet: wrong magic number");

            // Get packet length
            var packetLength = BitConverter.ToInt32(data, 4);
            if (data.Length < 8 + packetLength)
                throw new ArgumentException("Invalid packet: incomplete data");

            // Extract JSON data
            var jsonBytes = new byte[packetLength];
            Array.Copy(data, 8, jsonBytes, 0, packetLength);
            var json = Encoding.UTF8.GetString(jsonBytes);

            var packet = JsonSerializer.Deserialize<VPNPacket>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return packet ?? throw new InvalidOperationException("Failed to deserialize packet");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to deserialize VPN packet");
            throw;
        }
    }

    public VPNHandshakePacket CreateHandshakePacket(string clientVersion, string[] supportedCiphers, byte[] publicKey, byte[] certificate)
    {
        return new VPNHandshakePacket
        {
            Type = VPNPacketType.Handshake,
            SessionId = Guid.NewGuid().ToString(),
            ClientVersion = clientVersion,
            SupportedCiphers = supportedCiphers,
            ClientPublicKey = publicKey,
            ClientCertificate = certificate,
            Timestamp = DateTime.UtcNow
        };
    }

    public VPNKeyExchangePacket CreateKeyExchangePacket(byte[] serverPublicKey, byte[] serverCertificate, byte[] sharedSecret, string selectedCipher)
    {
        return new VPNKeyExchangePacket
        {
            Type = VPNPacketType.KeyExchange,
            SessionId = Guid.NewGuid().ToString(),
            ServerPublicKey = serverPublicKey,
            ServerCertificate = serverCertificate,
            SharedSecret = sharedSecret,
            SelectedCipher = selectedCipher,
            Timestamp = DateTime.UtcNow
        };
    }

    public VPNDataPacket CreateDataPacket(byte[] data, bool compress = false)
    {
        var packet = new VPNDataPacket
        {
            Type = VPNPacketType.Data,
            SessionId = Guid.NewGuid().ToString(),
            OriginalData = data,
            OriginalLength = data.Length,
            IsCompressed = compress,
            Timestamp = DateTime.UtcNow
        };

        if (compress)
        {
            packet.Data = CompressData(data);
        }
        else
        {
            packet.Data = data;
        }

        return packet;
    }

    private byte[] CompressData(byte[] data)
    {
        // Simple compression implementation
        // In production, use a proper compression algorithm like LZ4 or Deflate
        using var output = new MemoryStream();
        using var compressor = new System.IO.Compression.GZipStream(output, System.IO.Compression.CompressionMode.Compress);
        compressor.Write(data, 0, data.Length);
        compressor.Close();
        return output.ToArray();
    }

    private byte[] DecompressData(byte[] compressedData)
    {
        using var input = new MemoryStream(compressedData);
        using var decompressor = new System.IO.Compression.GZipStream(input, System.IO.Compression.CompressionMode.Decompress);
        using var output = new MemoryStream();
        decompressor.CopyTo(output);
        return output.ToArray();
    }

    public void Dispose()
    {
        _udpClient?.Dispose();
        _tcpClient?.Dispose();
        GC.SuppressFinalize(this);
    }
}