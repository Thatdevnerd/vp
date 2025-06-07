using Microsoft.Extensions.Logging;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using VPNCore.Interfaces;
using VPNCore.Models;

namespace VPNCore.WireGuard;

/// <summary>
/// WireGuard tunnel implementation for modern VPN connectivity
/// Replaces TAP interfaces with WireGuard's superior architecture
/// </summary>
public class WireGuardTunnel : IVPNTunnel, IDisposable
{
    private readonly ILogger<WireGuardTunnel> _logger;
    private readonly WireGuardConfiguration _config;
    private bool _isActive;
    private bool _disposed;
    
    // WireGuard state
    private WireGuardPeer? _peer;
    private WireGuardInterface? _interface;
    private CancellationTokenSource? _cancellationTokenSource;
    
    // Platform-specific handles
    private IntPtr _wintunSession = IntPtr.Zero;
    private FileStream? _tunStream; // Linux TUN stream
    
    public WireGuardTunnel(ILogger<WireGuardTunnel> logger, WireGuardConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public bool IsActive => _isActive;

    public async Task CreateTunnelAsync(string interfaceName, IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            _logger.LogInformation($"Creating WireGuard tunnel '{interfaceName}' with IP {virtualIP}");
            
            // Generate WireGuard keys if not provided
            if (_config.PrivateKey == null)
            {
                _config.GenerateKeys();
                _logger.LogInformation("Generated new WireGuard key pair");
            }

            // Create platform-specific tunnel
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await CreateWindowsWireGuardTunnelAsync(interfaceName, virtualIP, subnetMask);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await CreateLinuxWireGuardTunnelAsync(interfaceName, virtualIP, subnetMask);
            }
            else
            {
                throw new PlatformNotSupportedException("WireGuard tunnel only supports Windows and Linux");
            }

            _isActive = true;
            _cancellationTokenSource = new CancellationTokenSource();
            
            _logger.LogInformation($"WireGuard tunnel '{interfaceName}' created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to create WireGuard tunnel '{interfaceName}'");
            throw;
        }
    }

    private async Task CreateWindowsWireGuardTunnelAsync(string interfaceName, IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            _logger.LogInformation("Creating Windows WireGuard tunnel using WinTun driver");
            
            // Initialize WinTun session
            _wintunSession = WinTunNative.CreateSession(interfaceName);
            if (_wintunSession == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to create WinTun session. Ensure WinTun driver is installed.");
            }

            // Configure WireGuard interface
            _interface = new WireGuardInterface
            {
                Name = interfaceName,
                PrivateKey = _config.PrivateKey!,
                PublicKey = _config.PublicKey!,
                Address = virtualIP,
                SubnetMask = subnetMask,
                ListenPort = _config.ListenPort
            };

            // Configure interface IP
            await ConfigureWindowsWireGuardInterface(virtualIP, subnetMask);
            
            _logger.LogInformation($"Windows WireGuard tunnel configured with WinTun");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Windows WireGuard tunnel");
            throw;
        }
    }

    private async Task CreateLinuxWireGuardTunnelAsync(string interfaceName, IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            _logger.LogInformation("Creating Linux WireGuard tunnel using kernel module");
            
            // Create WireGuard interface using netlink
            await CreateLinuxWireGuardInterface(interfaceName);
            
            // Configure WireGuard interface
            _interface = new WireGuardInterface
            {
                Name = interfaceName,
                PrivateKey = _config.PrivateKey!,
                PublicKey = _config.PublicKey!,
                Address = virtualIP,
                SubnetMask = subnetMask,
                ListenPort = _config.ListenPort
            };

            // Configure interface using wg tool
            await ConfigureLinuxWireGuardInterface(interfaceName, virtualIP, subnetMask);
            
            _logger.LogInformation($"Linux WireGuard tunnel configured");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Linux WireGuard tunnel");
            throw;
        }
    }

    private async Task ConfigureWindowsWireGuardInterface(IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            // Configure IP address using netsh
            var interfaceName = _interface!.Name;
            var commands = new[]
            {
                $"netsh interface ip set address name=\"{interfaceName}\" static {virtualIP} {subnetMask}",
                $"netsh interface set interface name=\"{interfaceName}\" admin=enabled"
            };

            foreach (var cmd in commands)
            {
                await ExecuteCommandAsync(cmd);
            }

            _logger.LogInformation($"Windows WireGuard interface configured with IP {virtualIP}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure Windows WireGuard interface");
            throw;
        }
    }

    private async Task CreateLinuxWireGuardInterface(string interfaceName)
    {
        try
        {
            var commands = new[]
            {
                $"ip link add dev {interfaceName} type wireguard",
                $"ip link set {interfaceName} up"
            };

            foreach (var cmd in commands)
            {
                await ExecuteCommandAsync(cmd);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create Linux WireGuard interface");
            throw;
        }
    }

    private async Task ConfigureLinuxWireGuardInterface(string interfaceName, IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            var cidr = GetCidrFromMask(subnetMask);
            var commands = new[]
            {
                $"ip addr add {virtualIP}/{cidr} dev {interfaceName}",
                $"wg set {interfaceName} private-key <(echo '{Convert.ToBase64String(_config.PrivateKey!)}')",
                $"wg set {interfaceName} listen-port {_config.ListenPort}"
            };

            foreach (var cmd in commands)
            {
                await ExecuteCommandAsync(cmd);
            }

            _logger.LogInformation($"Linux WireGuard interface configured with IP {virtualIP}/{cidr}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure Linux WireGuard interface");
            throw;
        }
    }

    public async Task AddPeerAsync(WireGuardPeer peer)
    {
        try
        {
            _peer = peer;
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await AddWindowsPeerAsync(peer);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await AddLinuxPeerAsync(peer);
            }

            _logger.LogInformation($"Added WireGuard peer: {peer.PublicKey}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add WireGuard peer");
            throw;
        }
    }

    private async Task AddWindowsPeerAsync(WireGuardPeer peer)
    {
        // For Windows, we'll manage peer configuration through WinTun API
        // This is a simplified implementation - in practice, you'd use WireGuard's Windows service
        _logger.LogInformation($"Configuring Windows WireGuard peer: {peer.Endpoint}");
        await Task.CompletedTask;
    }

    private async Task AddLinuxPeerAsync(WireGuardPeer peer)
    {
        try
        {
            var allowedIPs = string.Join(",", peer.AllowedIPs.Select(ip => ip.ToString()));
            var cmd = $"wg set {_interface!.Name} peer {peer.PublicKey} endpoint {peer.Endpoint} allowed-ips {allowedIPs}";
            
            if (peer.PersistentKeepalive > 0)
            {
                cmd += $" persistent-keepalive {peer.PersistentKeepalive}";
            }

            await ExecuteCommandAsync(cmd);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add Linux WireGuard peer");
            throw;
        }
    }

    public async Task<byte[]> ReadPacketAsync(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            throw new InvalidOperationException("WireGuard tunnel is not active");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return await ReadWindowsPacketAsync(cancellationToken);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return await ReadLinuxPacketAsync(cancellationToken);
        }

        throw new PlatformNotSupportedException("Unsupported platform for WireGuard");
    }

    private async Task<byte[]> ReadWindowsPacketAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Read packet from WinTun session
            var packet = WinTunNative.ReadPacket(_wintunSession);
            if (packet != null && packet.Length > 0)
            {
                _logger.LogDebug($"Read {packet.Length} bytes from WinTun");
                return packet;
            }

            // If no packet available, wait briefly
            await Task.Delay(1, cancellationToken);
            return Array.Empty<byte>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Windows WireGuard packet");
            throw;
        }
    }

    private async Task<byte[]> ReadLinuxPacketAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_tunStream == null)
            {
                // Open TUN device for reading
                var tunPath = $"/sys/class/net/{_interface!.Name}/tun_flags";
                if (File.Exists(tunPath))
                {
                    _tunStream = new FileStream($"/dev/net/{_interface.Name}", FileMode.Open, FileAccess.ReadWrite);
                }
            }

            if (_tunStream != null)
            {
                var buffer = new byte[1500];
                int bytesRead = await _tunStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
                if (bytesRead > 0)
                {
                    var packet = new byte[bytesRead];
                    Array.Copy(buffer, packet, bytesRead);
                    _logger.LogDebug($"Read {bytesRead} bytes from Linux WireGuard");
                    return packet;
                }
            }

            await Task.Delay(1, cancellationToken);
            return Array.Empty<byte>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read Linux WireGuard packet");
            throw;
        }
    }

    public async Task WritePacketAsync(byte[] packet, CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            throw new InvalidOperationException("WireGuard tunnel is not active");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            await WriteWindowsPacketAsync(packet, cancellationToken);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            await WriteLinuxPacketAsync(packet, cancellationToken);
        }
        else
        {
            throw new PlatformNotSupportedException("Unsupported platform for WireGuard");
        }
    }

    private async Task WriteWindowsPacketAsync(byte[] packet, CancellationToken cancellationToken)
    {
        try
        {
            WinTunNative.WritePacket(_wintunSession, packet);
            _logger.LogDebug($"Wrote {packet.Length} bytes to WinTun");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write Windows WireGuard packet");
            throw;
        }
    }

    private async Task WriteLinuxPacketAsync(byte[] packet, CancellationToken cancellationToken)
    {
        try
        {
            if (_tunStream != null)
            {
                await _tunStream.WriteAsync(packet, 0, packet.Length, cancellationToken);
                _logger.LogDebug($"Wrote {packet.Length} bytes to Linux WireGuard");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write Linux WireGuard packet");
            throw;
        }
    }

    public async Task DestroyTunnelAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _isActive = false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await DestroyWindowsWireGuardTunnelAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await DestroyLinuxWireGuardTunnelAsync();
            }

            _logger.LogInformation($"WireGuard tunnel '{_interface?.Name}' destroyed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to destroy WireGuard tunnel");
            throw;
        }
    }

    private async Task DestroyWindowsWireGuardTunnelAsync()
    {
        try
        {
            if (_wintunSession != IntPtr.Zero)
            {
                WinTunNative.CloseSession(_wintunSession);
                _wintunSession = IntPtr.Zero;
            }

            _logger.LogInformation("Windows WireGuard tunnel cleanup completed");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Windows WireGuard cleanup");
        }
    }

    private async Task DestroyLinuxWireGuardTunnelAsync()
    {
        try
        {
            _tunStream?.Dispose();
            _tunStream = null;

            if (_interface != null)
            {
                var cmd = $"ip link delete {_interface.Name}";
                await ExecuteCommandAsync(cmd);
            }

            _logger.LogInformation("Linux WireGuard tunnel cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Linux WireGuard cleanup");
        }
    }

    private async Task ExecuteCommandAsync(string command)
    {
        try
        {
            _logger.LogDebug($"Executing command: {command}");
            
            using var process = new System.Diagnostics.Process();
            
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                process.StartInfo.FileName = "cmd.exe";
                process.StartInfo.Arguments = $"/c {command}";
            }
            else
            {
                process.StartInfo.FileName = "/bin/bash";
                process.StartInfo.Arguments = $"-c \"{command}\"";
            }
            
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                throw new InvalidOperationException($"Command failed: {command}. Error: {error}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to execute command: {command}");
            throw;
        }
    }

    private static int GetCidrFromMask(IPAddress mask)
    {
        var bytes = mask.GetAddressBytes();
        var cidr = 0;
        
        foreach (var b in bytes)
        {
            for (int i = 7; i >= 0; i--)
            {
                if ((b & (1 << i)) != 0)
                    cidr++;
                else
                    return cidr;
            }
        }
        
        return cidr;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            DestroyTunnelAsync().Wait();
            _cancellationTokenSource?.Dispose();
            _tunStream?.Dispose();
            _disposed = true;
        }
    }
}