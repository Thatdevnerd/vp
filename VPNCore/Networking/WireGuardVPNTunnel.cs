using System.Net;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VPNCore.Interfaces;
using VPNCore.WireGuard;

namespace VPNCore.Networking;

/// <summary>
/// Modern VPN tunnel implementation using WireGuard protocol
/// Replaces legacy TAP interfaces with WireGuard's superior architecture
/// </summary>
public class WireGuardVPNTunnel : IVPNTunnel, IDisposable
{
    private readonly ILogger<WireGuardVPNTunnel> _logger;
    private readonly WireGuardConfiguration _config;
    private WireGuardTunnel? _wireGuardTunnel;
    private bool _isActive;
    private bool _disposed;
    private string _interfaceName = string.Empty;
    private CancellationTokenSource? _cancellationTokenSource;

    // Fallback packet queues for testing environments
    private readonly Queue<byte[]> _incomingPackets = new();
    private readonly Queue<byte[]> _outgoingPackets = new();
    private readonly object _lockObject = new();

    public WireGuardVPNTunnel(ILogger<WireGuardVPNTunnel> logger, WireGuardConfiguration? config = null)
    {
        _logger = logger;
        _config = config ?? new WireGuardConfiguration();
    }

    public bool IsActive => _isActive;

    public async Task CreateTunnelAsync(string interfaceName, IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            _logger.LogInformation($"Creating WireGuard VPN tunnel '{interfaceName}' with IP {virtualIP}");
            _interfaceName = interfaceName;

            // Check if WireGuard is available on the system
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (!WinTunNative.IsWinTunAvailable())
                {
                    _logger.LogWarning("WinTun driver not available, falling back to mock implementation");
                    await CreateMockTunnelAsync(interfaceName, virtualIP, subnetMask);
                    return;
                }
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                if (!await IsWireGuardAvailableLinux())
                {
                    _logger.LogWarning("WireGuard kernel module not available, falling back to mock implementation");
                    await CreateMockTunnelAsync(interfaceName, virtualIP, subnetMask);
                    return;
                }
            }

            // Create actual WireGuard tunnel
            var wireGuardLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<WireGuardTunnel>();
            _wireGuardTunnel = new WireGuardTunnel(wireGuardLogger, _config);
            await _wireGuardTunnel.CreateTunnelAsync(interfaceName, virtualIP, subnetMask);

            _isActive = true;
            _cancellationTokenSource = new CancellationTokenSource();

            _logger.LogInformation($"WireGuard VPN tunnel '{interfaceName}' created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to create WireGuard VPN tunnel '{interfaceName}'");
            throw;
        }
    }

    private async Task CreateMockTunnelAsync(string interfaceName, IPAddress virtualIP, IPAddress subnetMask)
    {
        _logger.LogInformation($"Creating mock WireGuard tunnel for testing: {interfaceName}");
        
        // Generate keys for mock tunnel
        if (_config.PrivateKey == null)
        {
            _config.GenerateKeys();
        }

        _isActive = true;
        _cancellationTokenSource = new CancellationTokenSource();

        _logger.LogInformation($"Mock WireGuard tunnel '{interfaceName}' created with IP {virtualIP}");
        await Task.CompletedTask;
    }

    private async Task<bool> IsWireGuardAvailableLinux()
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "/bin/bash";
            process.StartInfo.Arguments = "-c \"modinfo wireguard > /dev/null 2>&1\"";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.CreateNoWindow = true;

            process.Start();
            await process.WaitForExitAsync();

            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task AddPeerAsync(string publicKey, IPEndPoint endpoint, List<IPAddress> allowedIPs)
    {
        try
        {
            var peer = new WireGuardPeer
            {
                PublicKey = publicKey,
                Endpoint = endpoint,
                AllowedIPs = allowedIPs,
                PersistentKeepalive = 25 // Standard keepalive interval
            };

            if (_wireGuardTunnel != null)
            {
                await _wireGuardTunnel.AddPeerAsync(peer);
            }

            _logger.LogInformation($"Added WireGuard peer: {publicKey} at {endpoint}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add WireGuard peer");
            throw;
        }
    }

    public async Task<byte[]> ReadPacketAsync(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            throw new InvalidOperationException("WireGuard tunnel is not active");

        // Use actual WireGuard tunnel if available
        if (_wireGuardTunnel != null)
        {
            return await _wireGuardTunnel.ReadPacketAsync(cancellationToken);
        }

        // Fallback to mock implementation
        return await ReadMockPacketAsync(cancellationToken);
    }

    private async Task<byte[]> ReadMockPacketAsync(CancellationToken cancellationToken)
    {
        // Mock packet reading for testing
        while (!cancellationToken.IsCancellationRequested)
        {
            lock (_lockObject)
            {
                if (_incomingPackets.Count > 0)
                {
                    var packet = _incomingPackets.Dequeue();
                    _logger.LogDebug($"Read mock packet: {packet.Length} bytes");
                    return packet;
                }
            }

            await Task.Delay(1, cancellationToken);
        }

        throw new OperationCanceledException("Read operation was cancelled");
    }

    public async Task WritePacketAsync(byte[] packet, CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            throw new InvalidOperationException("WireGuard tunnel is not active");

        // Use actual WireGuard tunnel if available
        if (_wireGuardTunnel != null)
        {
            await _wireGuardTunnel.WritePacketAsync(packet, cancellationToken);
            return;
        }

        // Fallback to mock implementation
        await WriteMockPacketAsync(packet, cancellationToken);
    }

    private async Task WriteMockPacketAsync(byte[] packet, CancellationToken cancellationToken)
    {
        // Mock packet writing for testing
        lock (_lockObject)
        {
            _outgoingPackets.Enqueue(packet);
        }

        _logger.LogDebug($"Wrote mock packet: {packet.Length} bytes");
        await Task.CompletedTask;
    }

    public async Task DestroyTunnelAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _isActive = false;

            if (_wireGuardTunnel != null)
            {
                await _wireGuardTunnel.DestroyTunnelAsync();
                _wireGuardTunnel.Dispose();
                _wireGuardTunnel = null;
            }

            // Clear mock queues
            lock (_lockObject)
            {
                _incomingPackets.Clear();
                _outgoingPackets.Clear();
            }

            _logger.LogInformation($"WireGuard VPN tunnel '{_interfaceName}' destroyed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to destroy WireGuard VPN tunnel '{_interfaceName}'");
            throw;
        }
    }

    /// <summary>
    /// Get WireGuard configuration for sharing with peers
    /// </summary>
    public WireGuardConfiguration GetConfiguration()
    {
        return _config;
    }

    /// <summary>
    /// Get the public key for this WireGuard instance
    /// </summary>
    public string GetPublicKey()
    {
        return _config.GetPublicKeyString();
    }

    /// <summary>
    /// Generate a new key pair
    /// </summary>
    public void GenerateNewKeys()
    {
        _config.GenerateKeys();
        _logger.LogInformation("Generated new WireGuard key pair");
    }

    /// <summary>
    /// Set the private key from a base64 string
    /// </summary>
    public void SetPrivateKey(string base64PrivateKey)
    {
        _config.SetPrivateKey(base64PrivateKey);
        _logger.LogInformation("Updated WireGuard private key");
    }

    /// <summary>
    /// Get tunnel statistics (mock implementation)
    /// </summary>
    public WireGuardStatus GetStatus()
    {
        return new WireGuardStatus
        {
            InterfaceName = _interfaceName,
            PublicKey = GetPublicKey(),
            ListenPort = _config.ListenPort,
            Peers = _config.Peers.Select(p => new WireGuardPeerStatus
            {
                PublicKey = p.PublicKey,
                Endpoint = p.Endpoint,
                AllowedIPs = p.AllowedIPs.Select(ip => ip.ToString()).ToList(),
                LastHandshake = p.LastHandshake,
                BytesReceived = p.BytesReceived,
                BytesSent = p.BytesSent
            }).ToList()
        };
    }

    /// <summary>
    /// Inject a mock packet for testing
    /// </summary>
    public void InjectMockPacket(byte[] packet)
    {
        if (_wireGuardTunnel == null) // Only for mock mode
        {
            lock (_lockObject)
            {
                _incomingPackets.Enqueue(packet);
            }
        }
    }

    /// <summary>
    /// Get mock outgoing packets for testing
    /// </summary>
    public List<byte[]> GetMockOutgoingPackets()
    {
        lock (_lockObject)
        {
            var packets = _outgoingPackets.ToList();
            _outgoingPackets.Clear();
            return packets;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            DestroyTunnelAsync().Wait();
            _cancellationTokenSource?.Dispose();
            _wireGuardTunnel?.Dispose();
            _disposed = true;
        }
    }
}