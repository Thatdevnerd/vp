using System.Net;
using Microsoft.Extensions.Logging;
using VPNCore.Networking;

namespace TunTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🔧 TUN Interface Test Program");
        Console.WriteLine("============================");
        
        // Check if running as root
        if (Environment.UserName != "root")
        {
            Console.WriteLine("⚠️  Warning: Not running as root. TUN interface creation may fail.");
            Console.WriteLine("   Use: sudo dotnet run");
            Console.WriteLine();
        }

        // Create logger
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug);
        });
        var logger = loggerFactory.CreateLogger<VPNTunnel>();

        // Test parameters
        string interfaceName = "tun-test";
        var virtualIP = IPAddress.Parse("10.8.0.1");
        var subnetMask = IPAddress.Parse("255.255.255.0");

        Console.WriteLine($"📋 Test Parameters:");
        Console.WriteLine($"   Interface: {interfaceName}");
        Console.WriteLine($"   Virtual IP: {virtualIP}");
        Console.WriteLine($"   Subnet Mask: {subnetMask}");
        Console.WriteLine();

        // First run mock tests that don't require actual TUN devices
        var mockTest = new MockTunTest(logger);
        await mockTest.RunMockTests();
        
        Console.WriteLine();
        Console.WriteLine("🚀 Attempting to create actual TUN interface...");
        
        var tunnel = new VPNTunnel(logger);
        
        try
        {
            await tunnel.CreateTunnelAsync(interfaceName, virtualIP, subnetMask);
            
            if (tunnel.IsActive)
            {
                Console.WriteLine("✅ TUN interface created successfully!");
                Console.WriteLine();
                
                // Test basic functionality
                await TestTunInterface(tunnel);
                
                Console.WriteLine();
                Console.WriteLine("🔍 Verifying interface configuration...");
                await VerifyInterface(interfaceName, virtualIP);
            }
            else
            {
                Console.WriteLine("❌ TUN interface creation failed - not active");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"   Inner: {ex.InnerException.Message}");
            }
            Console.WriteLine();
            Console.WriteLine("💡 This is expected in containerized environments.");
            Console.WriteLine("   The mock tests above verify the implementation logic.");
            Console.WriteLine("   For full testing, run on a system with TUN/TAP support:");
            Console.WriteLine("   1. Run as root: sudo dotnet run");
            Console.WriteLine("   2. Check TUN module: lsmod | grep tun");
            Console.WriteLine("   3. Load TUN module: sudo modprobe tun");
            Console.WriteLine("   4. Check device: ls -la /dev/net/tun");
        }
        finally
        {
            try
            {
                Console.WriteLine("🧹 Cleaning up...");
                await tunnel.DestroyTunnelAsync();
                tunnel.Dispose();
                Console.WriteLine("✅ Cleanup completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Cleanup warning: {ex.Message}");
            }
        }
        
        Console.WriteLine();
        Console.WriteLine("🏁 Test completed");
    }

    static async Task TestTunInterface(VPNTunnel tunnel)
    {
        Console.WriteLine("🧪 Testing TUN interface functionality...");
        
        try
        {
            // Test 1: Write a test packet
            Console.WriteLine("   📤 Testing packet write...");
            var testPacket = CreateTestIPPacket();
            await tunnel.WritePacketAsync(testPacket);
            Console.WriteLine("   ✅ Packet write successful");
            
            // Test 2: Try to read with timeout
            Console.WriteLine("   📥 Testing packet read (5 second timeout)...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            
            try
            {
                var readTask = tunnel.ReadPacketAsync(cts.Token);
                var packet = await readTask;
                Console.WriteLine($"   ✅ Read packet: {packet.Length} bytes");
                
                // Analyze the packet
                AnalyzePacket(packet);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("   ⏰ Read timeout (no incoming packets - this is normal for testing)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ❌ Test failed: {ex.Message}");
        }
    }

    static byte[] CreateTestIPPacket()
    {
        // Create a simple IPv4 packet (ping packet)
        var packet = new byte[28]; // IP header (20) + ICMP header (8)
        
        // IPv4 header
        packet[0] = 0x45; // Version (4) + IHL (5)
        packet[1] = 0x00; // Type of Service
        packet[2] = 0x00; packet[3] = 0x1C; // Total Length (28)
        packet[4] = 0x00; packet[5] = 0x01; // Identification
        packet[6] = 0x40; packet[7] = 0x00; // Flags + Fragment Offset
        packet[8] = 0x40; // TTL
        packet[9] = 0x01; // Protocol (ICMP)
        packet[10] = 0x00; packet[11] = 0x00; // Header Checksum (will be calculated)
        
        // Source IP: 10.8.0.2
        packet[12] = 10; packet[13] = 8; packet[14] = 0; packet[15] = 2;
        // Destination IP: 10.8.0.1
        packet[16] = 10; packet[17] = 8; packet[18] = 0; packet[19] = 1;
        
        // ICMP header (Echo Request)
        packet[20] = 0x08; // Type (Echo Request)
        packet[21] = 0x00; // Code
        packet[22] = 0x00; packet[23] = 0x00; // Checksum (will be calculated)
        packet[24] = 0x00; packet[25] = 0x01; // Identifier
        packet[26] = 0x00; packet[27] = 0x01; // Sequence Number
        
        // Calculate IP header checksum
        CalculateIPChecksum(packet, 0, 20);
        
        // Calculate ICMP checksum
        CalculateICMPChecksum(packet, 20, 8);
        
        return packet;
    }

    static void CalculateIPChecksum(byte[] packet, int offset, int length)
    {
        uint sum = 0;
        
        // Clear checksum field
        packet[offset + 10] = 0;
        packet[offset + 11] = 0;
        
        // Sum all 16-bit words
        for (int i = 0; i < length; i += 2)
        {
            sum += (uint)((packet[offset + i] << 8) + packet[offset + i + 1]);
        }
        
        // Add carry
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }
        
        // One's complement
        sum = ~sum & 0xFFFF;
        
        // Set checksum
        packet[offset + 10] = (byte)(sum >> 8);
        packet[offset + 11] = (byte)(sum & 0xFF);
    }

    static void CalculateICMPChecksum(byte[] packet, int offset, int length)
    {
        uint sum = 0;
        
        // Clear checksum field
        packet[offset + 2] = 0;
        packet[offset + 3] = 0;
        
        // Sum all 16-bit words
        for (int i = 0; i < length; i += 2)
        {
            sum += (uint)((packet[offset + i] << 8) + packet[offset + i + 1]);
        }
        
        // Add carry
        while ((sum >> 16) != 0)
        {
            sum = (sum & 0xFFFF) + (sum >> 16);
        }
        
        // One's complement
        sum = ~sum & 0xFFFF;
        
        // Set checksum
        packet[offset + 2] = (byte)(sum >> 8);
        packet[offset + 3] = (byte)(sum & 0xFF);
    }

    static void AnalyzePacket(byte[] packet)
    {
        if (packet.Length < 20)
        {
            Console.WriteLine($"   📊 Packet too short for IP header: {packet.Length} bytes");
            return;
        }

        // Parse IP header
        byte version = (byte)((packet[0] >> 4) & 0x0F);
        byte ihl = (byte)(packet[0] & 0x0F);
        byte protocol = packet[9];
        
        var srcIP = new IPAddress(new byte[] { packet[12], packet[13], packet[14], packet[15] });
        var dstIP = new IPAddress(new byte[] { packet[16], packet[17], packet[18], packet[19] });
        
        Console.WriteLine($"   📊 Packet Analysis:");
        Console.WriteLine($"      IP Version: {version}");
        Console.WriteLine($"      Header Length: {ihl * 4} bytes");
        Console.WriteLine($"      Protocol: {protocol} ({GetProtocolName(protocol)})");
        Console.WriteLine($"      Source: {srcIP}");
        Console.WriteLine($"      Destination: {dstIP}");
        Console.WriteLine($"      Total Length: {packet.Length} bytes");
    }

    static string GetProtocolName(byte protocol)
    {
        return protocol switch
        {
            1 => "ICMP",
            6 => "TCP",
            17 => "UDP",
            _ => "Unknown"
        };
    }

    static async Task VerifyInterface(string interfaceName, IPAddress expectedIP)
    {
        try
        {
            // Check if interface exists
            var checkCmd = $"ip addr show {interfaceName}";
            var result = await ExecuteCommandAsync(checkCmd);
            
            if (result.Contains(expectedIP.ToString()))
            {
                Console.WriteLine($"✅ Interface {interfaceName} configured correctly with IP {expectedIP}");
            }
            else
            {
                Console.WriteLine($"⚠️  Interface {interfaceName} exists but IP configuration may be incorrect");
            }
            
            Console.WriteLine("📋 Interface details:");
            var lines = result.Split('\n');
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Console.WriteLine($"   {line.Trim()}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️  Could not verify interface: {ex.Message}");
        }
    }

    static async Task<string> ExecuteCommandAsync(string command)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "/bin/bash";
        process.StartInfo.Arguments = $"-c \"{command}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed: {command}. Error: {error}");
        }

        return output;
    }
}