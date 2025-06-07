using Microsoft.Extensions.Logging;
using System.Net;
using VPNCore.Networking;

namespace TapTest;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🔧 Windows TAP Interface Test Program");
        Console.WriteLine("=====================================");
        
        // Setup logging
        using var loggerFactory = LoggerFactory.Create(builder =>
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        var logger = loggerFactory.CreateLogger<VPNTunnel>();

        // Test parameters
        string interfaceName = "tap-test";
        var virtualIP = IPAddress.Parse("10.8.0.1");
        var subnetMask = IPAddress.Parse("255.255.255.0");

        Console.WriteLine("📋 Test Parameters:");
        Console.WriteLine($"   Interface: {interfaceName}");
        Console.WriteLine($"   Virtual IP: {virtualIP}");
        Console.WriteLine($"   Subnet Mask: {subnetMask}");
        Console.WriteLine();

        // First run mock tests for Windows TAP
        var mockTest = new MockTapTest(logger);
        await mockTest.RunMockTests();
        
        Console.WriteLine();
        Console.WriteLine("🚀 Attempting to create actual Windows TAP interface...");
        
        var tunnel = new VPNTunnel(logger);
        
        try
        {
            await tunnel.CreateTunnelAsync(interfaceName, virtualIP, subnetMask);
            
            if (tunnel.IsActive)
            {
                Console.WriteLine("✅ TAP interface created successfully!");
                Console.WriteLine();
                
                // Test basic functionality
                await TestTapInterface(tunnel);
                
                Console.WriteLine();
                Console.WriteLine("🔍 Verifying interface configuration...");
                await VerifyWindowsInterface(interfaceName, virtualIP);
            }
            else
            {
                Console.WriteLine("❌ TAP interface creation failed - not active");
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
            Console.WriteLine("💡 Troubleshooting tips for Windows TAP:");
            Console.WriteLine("   1. Run as Administrator");
            Console.WriteLine("   2. Install TAP-Windows driver (from OpenVPN or similar)");
            Console.WriteLine("   3. Check if TAP adapter is enabled in Network Connections");
            Console.WriteLine("   4. Verify TAP adapter is not in use by another application");
            Console.WriteLine("   5. Try disabling/enabling the TAP adapter");
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

    static async Task TestTapInterface(VPNTunnel tunnel)
    {
        Console.WriteLine("🧪 Testing TAP interface functionality...");
        
        try
        {
            // Test packet creation and writing
            var testPacket = CreateTestPacket();
            Console.WriteLine($"📦 Created test packet: {testPacket.Length} bytes");
            
            await tunnel.WritePacketAsync(testPacket);
            Console.WriteLine("✅ Packet write test completed");
            
            // Test packet reading (with timeout)
            Console.WriteLine("📥 Testing packet read (5 second timeout)...");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            
            try
            {
                var receivedPacket = await tunnel.ReadPacketAsync(cts.Token);
                Console.WriteLine($"📦 Received packet: {receivedPacket.Length} bytes");
                AnalyzePacket(receivedPacket);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("⏰ Read timeout (this is normal if no traffic)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ TAP interface test failed: {ex.Message}");
        }
    }

    static byte[] CreateTestPacket()
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

    static void AnalyzePacket(byte[] packet)
    {
        if (packet.Length < 20)
        {
            Console.WriteLine("   📊 Packet too short for IP analysis");
            return;
        }
        
        Console.WriteLine("   📊 Packet Analysis:");
        Console.WriteLine($"      IP Version: {packet[0] >> 4}");
        Console.WriteLine($"      Header Length: {(packet[0] & 0x0F) * 4} bytes");
        Console.WriteLine($"      Protocol: {packet[9]}");
        Console.WriteLine($"      Source: {packet[12]}.{packet[13]}.{packet[14]}.{packet[15]}");
        Console.WriteLine($"      Destination: {packet[16]}.{packet[17]}.{packet[18]}.{packet[19]}");
        Console.WriteLine($"      Total Length: {(packet[2] << 8) | packet[3]} bytes");
    }

    static async Task VerifyWindowsInterface(string interfaceName, IPAddress expectedIP)
    {
        try
        {
            Console.WriteLine("🔍 Verifying Windows network interface...");
            
            // Use netsh to show interface configuration
            var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "netsh";
            process.StartInfo.Arguments = "interface ip show addresses";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.CreateNoWindow = true;
            
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            if (output.Contains(expectedIP.ToString()))
            {
                Console.WriteLine($"✅ Found expected IP {expectedIP} in interface configuration");
            }
            else
            {
                Console.WriteLine($"⚠️  Expected IP {expectedIP} not found in interface configuration");
            }
            
            Console.WriteLine("📋 Current interface configuration:");
            var lines = output.Split('\n');
            foreach (var line in lines.Take(10)) // Show first 10 lines
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Console.WriteLine($"   {line.Trim()}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Interface verification failed: {ex.Message}");
        }
    }
}

public class MockTapTest
{
    private readonly ILogger _logger;

    public MockTapTest(ILogger logger)
    {
        _logger = logger;
    }

    public async Task RunMockTests()
    {
        Console.WriteLine("🧪 Running Mock Windows TAP Tests");
        Console.WriteLine("==================================");
        Console.WriteLine();

        // Test TAP constants and structures
        TestTapConstants();
        
        // Test packet creation and analysis
        TestPacketOperations();
        
        // Test Windows command generation
        TestWindowsCommands();
        
        Console.WriteLine("✅ All mock TAP tests completed successfully!");
        
        await Task.CompletedTask;
    }

    private void TestTapConstants()
    {
        Console.WriteLine("🔧 Testing Windows TAP constants...");
        
        // Test TAP control codes
        uint setMediaStatus = (0x00000022 << 16) | (6 << 2) | 0;
        uint configTun = (0x00000022 << 16) | (10 << 2) | 0;
        
        Console.WriteLine($"   TAP_IOCTL_SET_MEDIA_STATUS: 0x{setMediaStatus:X8}");
        Console.WriteLine($"   TAP_IOCTL_CONFIG_TUN: 0x{configTun:X8}");
        Console.WriteLine($"   GENERIC_READ: 0x{0x80000000:X8}");
        Console.WriteLine($"   GENERIC_WRITE: 0x{0x40000000:X8}");
        Console.WriteLine("   ✅ TAP constants test completed");
        Console.WriteLine();
    }

    private void TestPacketOperations()
    {
        Console.WriteLine("📦 Testing Windows TAP packet operations...");
        
        // Create test packet
        var packet = CreateMockPacket();
        Console.WriteLine($"   Created test packet: {packet.Length} bytes");
        
        // Analyze packet
        if (packet.Length >= 20)
        {
            Console.WriteLine("   📊 Packet Analysis:");
            Console.WriteLine($"      IP Version: {packet[0] >> 4}");
            Console.WriteLine($"      Protocol: {packet[9]}");
            Console.WriteLine($"      Source: {packet[12]}.{packet[13]}.{packet[14]}.{packet[15]}");
            Console.WriteLine($"      Destination: {packet[16]}.{packet[17]}.{packet[18]}.{packet[19]}");
        }
        
        Console.WriteLine("   ✅ Packet operations test completed");
        Console.WriteLine();
    }

    private void TestWindowsCommands()
    {
        Console.WriteLine("⚙️  Testing Windows command generation...");
        
        var adapterName = "TAP-Windows Adapter V9";
        var ip = "10.8.0.1";
        var mask = "255.255.255.0";
        
        var commands = new[]
        {
            $"netsh interface set interface name=\"{adapterName}\" admin=enabled",
            $"netsh interface ip set address name=\"{adapterName}\" static {ip} {mask}",
            $"netsh interface ip add address name=\"{adapterName}\" addr={ip} mask={mask}",
            $"route add 10.8.0.0 mask {mask} {ip}"
        };
        
        Console.WriteLine("   Expected Windows commands:");
        foreach (var cmd in commands)
        {
            Console.WriteLine($"     {cmd}");
        }
        
        Console.WriteLine("   ✅ Command generation test completed");
        Console.WriteLine();
    }

    private byte[] CreateMockPacket()
    {
        // Simple IP packet for testing
        var packet = new byte[20]; // IP header only
        
        packet[0] = 0x45; // Version 4, Header Length 5
        packet[9] = 0x06; // Protocol TCP
        packet[12] = 10; packet[13] = 8; packet[14] = 0; packet[15] = 2; // Source
        packet[16] = 10; packet[17] = 8; packet[18] = 0; packet[19] = 1; // Dest
        
        return packet;
    }
}