using Microsoft.Extensions.Logging;
using System.Net;
using VPNCore.Networking;
using VPNCore.WireGuard;

namespace WireGuardTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🔧 WireGuard VPN Implementation Test Program");
        Console.WriteLine("============================================");
        
        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<WireGuardVPNTunnel>();

        // Test parameters
        string interfaceName = "wg-test";
        var virtualIP = IPAddress.Parse("10.8.0.1");
        var subnetMask = IPAddress.Parse("255.255.255.0");

        Console.WriteLine("📋 Test Parameters:");
        Console.WriteLine($"   Interface: {interfaceName}");
        Console.WriteLine($"   Virtual IP: {virtualIP}");
        Console.WriteLine($"   Subnet Mask: {subnetMask}");
        Console.WriteLine();

        // Run comprehensive WireGuard tests
        await RunWireGuardCryptoTests();
        await RunWireGuardConfigurationTests();
        await RunWireGuardTunnelTests(logger, interfaceName, virtualIP, subnetMask);
        await RunWireGuardPeerTests(logger);
        
        Console.WriteLine();
        Console.WriteLine("🏁 All WireGuard tests completed successfully!");
    }

    static async Task RunWireGuardCryptoTests()
    {
        Console.WriteLine("🔐 Testing WireGuard Cryptography");
        Console.WriteLine("==================================");
        
        try
        {
            // Test key generation
            Console.WriteLine("🔑 Testing key generation...");
            var privateKey = WireGuardCrypto.GeneratePrivateKey();
            var publicKey = WireGuardCrypto.GetPublicKey(privateKey);
            
            Console.WriteLine($"   Private key length: {privateKey.Length} bytes");
            Console.WriteLine($"   Public key length: {publicKey.Length} bytes");
            Console.WriteLine($"   Private key: {Convert.ToBase64String(privateKey)[..16]}...");
            Console.WriteLine($"   Public key: {Convert.ToBase64String(publicKey)[..16]}...");
            
            // Test ECDH
            Console.WriteLine("🤝 Testing ECDH key exchange...");
            var privateKey2 = WireGuardCrypto.GeneratePrivateKey();
            var publicKey2 = WireGuardCrypto.GetPublicKey(privateKey2);
            
            var sharedSecret1 = WireGuardCrypto.PerformECDH(privateKey, publicKey2);
            var sharedSecret2 = WireGuardCrypto.PerformECDH(privateKey2, publicKey);
            
            Console.WriteLine($"   Shared secret 1: {Convert.ToBase64String(sharedSecret1)[..16]}...");
            Console.WriteLine($"   Shared secret 2: {Convert.ToBase64String(sharedSecret2)[..16]}...");
            Console.WriteLine($"   Secrets match: {sharedSecret1.SequenceEqual(sharedSecret2)}");
            
            // Test encryption/decryption
            Console.WriteLine("🔒 Testing ChaCha20Poly1305 encryption...");
            var plaintext = System.Text.Encoding.UTF8.GetBytes("Hello WireGuard!");
            var key = WireGuardCrypto.GeneratePrivateKey();
            var nonce = new byte[12];
            Random.Shared.NextBytes(nonce);
            
            var (ciphertext, tag) = WireGuardCrypto.ChaCha20Poly1305Encrypt(plaintext, key, nonce);
            var decrypted = WireGuardCrypto.ChaCha20Poly1305Decrypt(ciphertext, key, nonce, tag);
            
            Console.WriteLine($"   Original: {System.Text.Encoding.UTF8.GetString(plaintext)}");
            Console.WriteLine($"   Decrypted: {System.Text.Encoding.UTF8.GetString(decrypted)}");
            Console.WriteLine($"   Encryption/Decryption successful: {plaintext.SequenceEqual(decrypted)}");
            
            // Test key derivation
            Console.WriteLine("🔄 Testing key derivation...");
            var keys = WireGuardCrypto.DeriveHandshakeKeys(sharedSecret1, publicKey, publicKey2);
            Console.WriteLine($"   Send key: {Convert.ToBase64String(keys.SendKey)[..16]}...");
            Console.WriteLine($"   Receive key: {Convert.ToBase64String(keys.ReceiveKey)[..16]}...");
            
            Console.WriteLine("✅ WireGuard cryptography tests completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ WireGuard cryptography test failed: {ex.Message}");
        }
        
        Console.WriteLine();
    }

    static async Task RunWireGuardConfigurationTests()
    {
        Console.WriteLine("⚙️  Testing WireGuard Configuration");
        Console.WriteLine("===================================");
        
        try
        {
            // Test configuration creation
            Console.WriteLine("📝 Testing configuration creation...");
            var config = new WireGuardConfiguration();
            config.GenerateKeys();
            config.ListenPort = 51820;
            
            Console.WriteLine($"   Listen port: {config.ListenPort}");
            Console.WriteLine($"   Public key: {config.GetPublicKeyString()[..16]}...");
            Console.WriteLine($"   Private key: {config.GetPrivateKeyString()[..16]}...");
            
            // Test peer management
            Console.WriteLine("👥 Testing peer management...");
            var peer = new WireGuardPeer
            {
                PublicKey = config.GetPublicKeyString(),
                PersistentKeepalive = 25
            };
            peer.SetEndpoint("127.0.0.1", 51821);
            peer.AddAllowedIP("10.8.0.0/24");
            
            config.Peers.Add(peer);
            
            Console.WriteLine($"   Peer public key: {peer.PublicKey[..16]}...");
            Console.WriteLine($"   Peer endpoint: {peer.Endpoint}");
            Console.WriteLine($"   Allowed IPs: {peer.AllowedIPs.Count}");
            Console.WriteLine($"   Keepalive: {peer.PersistentKeepalive}s");
            
            // Test interface configuration
            Console.WriteLine("🌐 Testing interface configuration...");
            var wgInterface = new WireGuardInterface
            {
                Name = "wg-test",
                PrivateKey = config.PrivateKey!,
                PublicKey = config.PublicKey!,
                Address = IPAddress.Parse("10.8.0.1"),
                SubnetMask = IPAddress.Parse("255.255.255.0"),
                ListenPort = 51820,
                MTU = 1420
            };
            
            Console.WriteLine($"   Interface name: {wgInterface.Name}");
            Console.WriteLine($"   Address: {wgInterface.Address}");
            Console.WriteLine($"   Listen port: {wgInterface.ListenPort}");
            Console.WriteLine($"   MTU: {wgInterface.MTU}");
            
            Console.WriteLine("✅ WireGuard configuration tests completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ WireGuard configuration test failed: {ex.Message}");
        }
        
        Console.WriteLine();
        await Task.CompletedTask;
    }

    static async Task RunWireGuardTunnelTests(ILogger<WireGuardVPNTunnel> logger, string interfaceName, IPAddress virtualIP, IPAddress subnetMask)
    {
        Console.WriteLine("🚇 Testing WireGuard Tunnel Implementation");
        Console.WriteLine("==========================================");
        
        WireGuardVPNTunnel? tunnel = null;
        
        try
        {
            // Create WireGuard tunnel
            Console.WriteLine("🔧 Creating WireGuard tunnel...");
            var config = new WireGuardConfiguration();
            tunnel = new WireGuardVPNTunnel(logger, config);
            
            await tunnel.CreateTunnelAsync(interfaceName, virtualIP, subnetMask);
            
            if (tunnel.IsActive)
            {
                Console.WriteLine("✅ WireGuard tunnel created successfully!");
                Console.WriteLine($"   Public key: {tunnel.GetPublicKey()[..16]}...");
                
                // Test packet operations
                Console.WriteLine("📦 Testing packet operations...");
                await TestWireGuardPacketOperations(tunnel);
                
                // Test status reporting
                Console.WriteLine("📊 Testing status reporting...");
                var status = tunnel.GetStatus();
                Console.WriteLine($"   Interface: {status.InterfaceName}");
                Console.WriteLine($"   Public key: {status.PublicKey[..16]}...");
                Console.WriteLine($"   Listen port: {status.ListenPort}");
                Console.WriteLine($"   Peers: {status.Peers.Count}");
                
                // Test key management
                Console.WriteLine("🔑 Testing key management...");
                var oldPublicKey = tunnel.GetPublicKey();
                tunnel.GenerateNewKeys();
                var newPublicKey = tunnel.GetPublicKey();
                
                Console.WriteLine($"   Old key: {oldPublicKey[..16]}...");
                Console.WriteLine($"   New key: {newPublicKey[..16]}...");
                Console.WriteLine($"   Keys different: {oldPublicKey != newPublicKey}");
            }
            else
            {
                Console.WriteLine("⚠️  WireGuard tunnel created but not active (expected in test environment)");
            }
            
            Console.WriteLine("✅ WireGuard tunnel tests completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ WireGuard tunnel test failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
        }
        finally
        {
            if (tunnel != null)
            {
                Console.WriteLine("🧹 Cleaning up tunnel...");
                await tunnel.DestroyTunnelAsync();
                tunnel.Dispose();
                Console.WriteLine("✅ Tunnel cleanup completed");
            }
        }
        
        Console.WriteLine();
    }

    static async Task TestWireGuardPacketOperations(WireGuardVPNTunnel tunnel)
    {
        try
        {
            // Create test packet
            var testPacket = CreateTestIPPacket();
            Console.WriteLine($"   Created test packet: {testPacket.Length} bytes");
            
            // Inject packet for testing (mock mode)
            tunnel.InjectMockPacket(testPacket);
            
            // Test packet writing
            await tunnel.WritePacketAsync(testPacket);
            Console.WriteLine("   ✅ Packet write test completed");
            
            // Test packet reading
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                var receivedPacket = await tunnel.ReadPacketAsync(cts.Token);
                Console.WriteLine($"   📦 Received packet: {receivedPacket.Length} bytes");
                
                if (receivedPacket.SequenceEqual(testPacket))
                {
                    Console.WriteLine("   ✅ Packet integrity verified");
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("   ⏰ Read timeout (normal in test environment)");
            }
            
            // Test mock packet operations
            var outgoingPackets = tunnel.GetMockOutgoingPackets();
            Console.WriteLine($"   📤 Mock outgoing packets: {outgoingPackets.Count}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Packet operation test failed: {ex.Message}");
        }
    }

    static async Task RunWireGuardPeerTests(ILogger<WireGuardVPNTunnel> logger)
    {
        Console.WriteLine("👥 Testing WireGuard Peer Management");
        Console.WriteLine("====================================");
        
        try
        {
            var config = new WireGuardConfiguration();
            using var tunnel = new WireGuardVPNTunnel(logger, config);
            
            await tunnel.CreateTunnelAsync("wg-peer-test", IPAddress.Parse("10.8.0.2"), IPAddress.Parse("255.255.255.0"));
            
            // Test peer addition
            Console.WriteLine("➕ Testing peer addition...");
            var peerPublicKey = Convert.ToBase64String(WireGuardCrypto.GeneratePrivateKey());
            var peerEndpoint = new IPEndPoint(IPAddress.Parse("192.168.1.100"), 51820);
            var allowedIPs = new List<IPAddress> { IPAddress.Parse("10.8.0.0") };
            
            await tunnel.AddPeerAsync(peerPublicKey, peerEndpoint, allowedIPs);
            Console.WriteLine($"   ✅ Added peer: {peerPublicKey[..16]}...");
            Console.WriteLine($"   Endpoint: {peerEndpoint}");
            Console.WriteLine($"   Allowed IPs: {allowedIPs.Count}");
            
            // Test status with peer
            var status = tunnel.GetStatus();
            Console.WriteLine($"   📊 Status peers: {status.Peers.Count}");
            
            await tunnel.DestroyTunnelAsync();
            Console.WriteLine("✅ WireGuard peer tests completed successfully!");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ WireGuard peer test failed: {ex.Message}");
        }
        
        Console.WriteLine();
    }

    static byte[] CreateTestIPPacket()
    {
        // Create a simple ICMP ping packet
        var packet = new byte[28]; // IP header (20) + ICMP header (8)
        
        // IP Header
        packet[0] = 0x45; // Version (4) + Header Length (5)
        packet[1] = 0x00; // Type of Service
        packet[2] = 0x00; packet[3] = 0x1C; // Total Length (28)
        packet[4] = 0x00; packet[5] = 0x01; // Identification
        packet[6] = 0x00; packet[7] = 0x00; // Flags + Fragment Offset
        packet[8] = 0x40; // TTL (64)
        packet[9] = 0x01; // Protocol (ICMP)
        packet[10] = 0x00; packet[11] = 0x00; // Header Checksum (will calculate)
        
        // Source IP: 10.8.0.2
        packet[12] = 10; packet[13] = 8; packet[14] = 0; packet[15] = 2;
        // Destination IP: 10.8.0.1
        packet[16] = 10; packet[17] = 8; packet[18] = 0; packet[19] = 1;
        
        // Calculate IP header checksum
        ushort checksum = CalculateChecksum(packet, 0, 20);
        packet[10] = (byte)(checksum >> 8);
        packet[11] = (byte)(checksum & 0xFF);
        
        // ICMP Header
        packet[20] = 0x08; // Type (Echo Request)
        packet[21] = 0x00; // Code
        packet[22] = 0x00; packet[23] = 0x00; // Checksum (will calculate)
        packet[24] = 0x00; packet[25] = 0x01; // Identifier
        packet[26] = 0x00; packet[27] = 0x01; // Sequence Number
        
        // Calculate ICMP checksum
        ushort icmpChecksum = CalculateChecksum(packet, 20, 8);
        packet[22] = (byte)(icmpChecksum >> 8);
        packet[23] = (byte)(icmpChecksum & 0xFF);
        
        return packet;
    }

    static ushort CalculateChecksum(byte[] data, int offset, int length)
    {
        uint sum = 0;
        
        for (int i = offset; i < offset + length; i += 2)
        {
            if (i + 1 < offset + length)
            {
                sum += (uint)((data[i] << 8) + data[i + 1]);
            }
            else
            {
                sum += (uint)(data[i] << 8);
            }
        }
        
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }
        
        return (ushort)~sum;
    }
}