using Microsoft.Extensions.Logging;
using VPNCore.Cryptography;
using VPNCore.Models;

namespace VPNCore.Processing;

public class VPNPacketProcessor
{
    private readonly ICryptoProvider _cryptoProvider;
    private readonly ICompressionProvider _compressionProvider;
    private readonly ILogger<VPNPacketProcessor> _logger;

    public VPNPacketProcessor(
        ICryptoProvider cryptoProvider,
        ICompressionProvider compressionProvider,
        ILogger<VPNPacketProcessor> logger)
    {
        _cryptoProvider = cryptoProvider;
        _compressionProvider = compressionProvider;
        _logger = logger;
    }

    public VPNPacket ProcessOutgoingPacket(VPNPacket packet, VPNSession session, bool compress = true)
    {
        try
        {
            var processedPacket = new VPNPacket
            {
                Type = packet.Type,
                SessionId = session.SessionId,
                SequenceNumber = ++session.LastSequenceNumber,
                Timestamp = DateTime.UtcNow,
                Data = packet.Data
            };

            // Compress data if beneficial and enabled
            if (compress && packet.Type == VPNPacketType.Data && _compressionProvider.IsCompressionBeneficial(packet.Data))
            {
                processedPacket.Data = _compressionProvider.Compress(packet.Data);
                if (processedPacket is VPNDataPacket dataPacket)
                {
                    dataPacket.IsCompressed = true;
                    dataPacket.OriginalLength = packet.Data.Length;
                }
            }

            // Encrypt the packet
            if (session.EncryptionKey.Length > 0)
            {
                var iv = _cryptoProvider.GenerateIV(128);
                processedPacket.Data = _cryptoProvider.Encrypt(processedPacket.Data, session.EncryptionKey, iv);
                processedPacket.IV = iv;
                processedPacket.IsEncrypted = true;

                // Add authentication tag
                var authData = CombinePacketDataForAuth(processedPacket);
                processedPacket.AuthTag = _cryptoProvider.ComputeHash(authData, session.AuthenticationKey);
            }

            session.BytesSent += processedPacket.Data.Length;
            _logger.LogDebug("Processed outgoing {PacketType} packet for session {SessionId}", 
                packet.Type, session.SessionId);

            return processedPacket;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process outgoing packet for session {SessionId}", session.SessionId);
            throw;
        }
    }

    public VPNPacket ProcessIncomingPacket(VPNPacket packet, VPNSession session)
    {
        try
        {
            var processedPacket = new VPNPacket
            {
                Type = packet.Type,
                SessionId = packet.SessionId,
                SequenceNumber = packet.SequenceNumber,
                Timestamp = packet.Timestamp,
                Data = packet.Data,
                IV = packet.IV,
                AuthTag = packet.AuthTag,
                IsEncrypted = packet.IsEncrypted
            };

            // Verify authentication if encrypted
            if (packet.IsEncrypted && session.AuthenticationKey.Length > 0)
            {
                var authData = CombinePacketDataForAuth(packet);
                if (!_cryptoProvider.VerifyHash(authData, packet.AuthTag, session.AuthenticationKey))
                {
                    throw new UnauthorizedAccessException("Packet authentication failed");
                }
            }

            // Decrypt the packet
            if (packet.IsEncrypted && session.EncryptionKey.Length > 0)
            {
                processedPacket.Data = _cryptoProvider.Decrypt(packet.Data, session.EncryptionKey, packet.IV);
                processedPacket.IsEncrypted = false;
            }

            // Decompress data if compressed
            if (processedPacket is VPNDataPacket dataPacket && dataPacket.IsCompressed)
            {
                processedPacket.Data = _compressionProvider.Decompress(processedPacket.Data);
                dataPacket.IsCompressed = false;
            }

            session.BytesReceived += packet.Data.Length;
            session.LastActivity = DateTime.UtcNow;

            _logger.LogDebug("Processed incoming {PacketType} packet for session {SessionId}", 
                packet.Type, session.SessionId);

            return processedPacket;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process incoming packet for session {SessionId}", session.SessionId);
            throw;
        }
    }

    public VPNHandshakePacket CreateHandshakePacket(string clientVersion, string[] supportedCiphers, byte[] publicKey, byte[] certificate)
    {
        return new VPNHandshakePacket
        {
            Type = VPNPacketType.Handshake,
            ClientVersion = clientVersion,
            SupportedCiphers = supportedCiphers,
            ClientPublicKey = publicKey,
            ClientCertificate = certificate,
            Timestamp = DateTime.UtcNow
        };
    }

    public VPNKeyExchangePacket CreateKeyExchangePacket(byte[] serverPublicKey, byte[] serverCertificate, string selectedCipher)
    {
        return new VPNKeyExchangePacket
        {
            Type = VPNPacketType.KeyExchange,
            ServerPublicKey = serverPublicKey,
            ServerCertificate = serverCertificate,
            SelectedCipher = selectedCipher,
            Timestamp = DateTime.UtcNow
        };
    }

    public VPNPacket CreateKeepAlivePacket(string sessionId)
    {
        return new VPNPacket
        {
            Type = VPNPacketType.KeepAlive,
            SessionId = sessionId,
            Timestamp = DateTime.UtcNow
        };
    }

    public VPNPacket CreateAcknowledgmentPacket(string sessionId, uint sequenceNumber)
    {
        return new VPNPacket
        {
            Type = VPNPacketType.Acknowledgment,
            SessionId = sessionId,
            SequenceNumber = sequenceNumber,
            Timestamp = DateTime.UtcNow
        };
    }

    private static byte[] CombinePacketDataForAuth(VPNPacket packet)
    {
        var sessionIdBytes = System.Text.Encoding.UTF8.GetBytes(packet.SessionId);
        var sequenceBytes = BitConverter.GetBytes(packet.SequenceNumber);
        var timestampBytes = BitConverter.GetBytes(packet.Timestamp.ToBinary());
        var typeBytes = new[] { (byte)packet.Type };

        var combined = new byte[sessionIdBytes.Length + sequenceBytes.Length + timestampBytes.Length + typeBytes.Length + packet.Data.Length];
        var offset = 0;

        Array.Copy(sessionIdBytes, 0, combined, offset, sessionIdBytes.Length);
        offset += sessionIdBytes.Length;

        Array.Copy(sequenceBytes, 0, combined, offset, sequenceBytes.Length);
        offset += sequenceBytes.Length;

        Array.Copy(timestampBytes, 0, combined, offset, timestampBytes.Length);
        offset += timestampBytes.Length;

        Array.Copy(typeBytes, 0, combined, offset, typeBytes.Length);
        offset += typeBytes.Length;

        Array.Copy(packet.Data, 0, combined, offset, packet.Data.Length);

        return combined;
    }
}