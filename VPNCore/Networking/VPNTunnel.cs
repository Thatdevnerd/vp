using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using VPNCore.Interfaces;
using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Management;
using System.Text;

namespace VPNCore.Networking;

public class VPNTunnel : IVPNTunnel, IDisposable
{
    private readonly ILogger<VPNTunnel> _logger;
    private bool _isActive;
    private string _interfaceName = string.Empty;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly Queue<byte[]> _incomingPackets = new();
    private readonly Queue<byte[]> _outgoingPackets = new();
    private readonly object _lockObject = new();
    
    // Windows TAP interface handle
    private SafeFileHandle? _tapHandle;
    private FileStream? _tapStream;
    
    // Linux TUN interface handle
    private FileStream? _tunStream;
    private int _tunFd = -1;

    // Linux system calls for TUN interface
    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags);
    
    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
    
    [DllImport("libc", SetLastError = true)]
    private static extern int ioctl(int fd, uint request, IntPtr argp);
    
    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr strerror(int errnum);
    
    [DllImport("libc", SetLastError = true)]
    private static extern int read(int fd, byte[] buf, int count);
    
    [DllImport("libc", SetLastError = true)]
    private static extern int write(int fd, byte[] buf, int count);

    // Linux TUN/TAP constants
    public const int O_RDWR = 2;
    public const uint TUNSETIFF = 0x400454ca;
    public const short IFF_TUN = 0x0001;
    public const short IFF_NO_PI = 0x1000;

    // Structure for TUN interface configuration
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct ifreq
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string ifr_name;
        public short ifr_flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 22)]
        public byte[] padding;
    }

    // Windows API imports for TAP interface
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,a
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        IntPtr lpInBuffer,
        uint nInBufferSize,
        IntPtr lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_ATTRIBUTE_SYSTEM = 0x00000004;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    // TAP-Windows control codes
    private static uint TAP_CONTROL_CODE(uint request, uint method) => 
        (0x00000022 << 16) | ((request) << 2) | (method);
    private const uint METHOD_BUFFERED = 0;
    private const uint TAP_IOCTL_GET_MAC = 1;
    private const uint TAP_IOCTL_GET_VERSION = 2;
    private const uint TAP_IOCTL_GET_MTU = 3;
    private const uint TAP_IOCTL_GET_INFO = 4;
    private const uint TAP_IOCTL_CONFIG_POINT_TO_POINT = 5;
    private const uint TAP_IOCTL_SET_MEDIA_STATUS = 6;
    private const uint TAP_IOCTL_CONFIG_DHCP_MASQ = 7;
    private const uint TAP_IOCTL_GET_LOG_LINE = 8;
    private const uint TAP_IOCTL_CONFIG_DHCP_SET_OPT = 9;
    private const uint TAP_IOCTL_CONFIG_TUN = 10;

    public VPNTunnel(ILogger<VPNTunnel> logger)
    {
        _logger = logger;
    }

    public bool IsActive => _isActive;

    public async Task CreateTunnelAsync(string interfaceName, IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            _interfaceName = interfaceName;
            _cancellationTokenSource = new CancellationTokenSource();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await CreateWindowsTAPAsync(virtualIP, subnetMask);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await CreateLinuxTUNAsync(virtualIP, subnetMask);
            }
            else
            {
                throw new PlatformNotSupportedException("Unsupported operating system");
            }

            _isActive = true;
            _logger.LogInformation($"VPN TAP interface '{interfaceName}' created successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to create VPN TAP interface '{interfaceName}'");
            throw;
        }
    }

    private async Task CreateWindowsTAPAsync(IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            // Find TAP-Windows adapter GUID
            string? deviceGuid = GetTAPDeviceGuid();
            if (string.IsNullOrEmpty(deviceGuid))
            {
                throw new InvalidOperationException("No TAP-Windows adapter found. Please install TAP-Windows driver from OpenVPN or similar.");
            }

            _logger.LogInformation($"Found TAP device GUID: {deviceGuid}");

            // Try multiple device path formats for compatibility
            string[] devicePaths = {
                $"\\\\.\\{deviceGuid}.tap",
                $"\\\\.\\Global\\{deviceGuid}.tap",
                $"\\\\.\\{{{deviceGuid}}}.tap"
            };

            SafeFileHandle? tapHandle = null;
            string? successfulPath = null;

            foreach (string devicePath in devicePaths)
            {
                _logger.LogDebug($"Trying TAP device path: {devicePath}");
                
                tapHandle = CreateFile(
                    devicePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_ATTRIBUTE_SYSTEM | FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero);

                if (!tapHandle.IsInvalid)
                {
                    successfulPath = devicePath;
                    _logger.LogInformation($"Successfully opened TAP device: {devicePath}");
                    break;
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger.LogDebug($"Failed to open {devicePath}: Win32 error {error}");
                    tapHandle.Dispose();
                }
            }

            if (tapHandle == null || tapHandle.IsInvalid)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), 
                    "Failed to open TAP device with any known path format. Ensure TAP-Windows driver is installed and adapter is enabled.");
            }

            _tapHandle = tapHandle;

            // Get TAP driver version for compatibility
            await GetTAPDriverInfo();

            // Configure TAP device for TUN mode (point-to-point)
            await ConfigureTAPForTUN(virtualIP, subnetMask);

            // Set media status to connected
            await SetTAPMediaStatus(true);

            // Create FileStream for reading/writing
            _tapStream = new FileStream(
                handle: _tapHandle, 
                access: FileAccess.ReadWrite, 
                bufferSize: 1500, 
                isAsync: true);

            // Configure network interface using Windows commands
            await ConfigureWindowsInterface(virtualIP, subnetMask);

            _logger.LogInformation($"Windows TAP interface '{_interfaceName}' configured successfully with IP {virtualIP}");
        }
        catch (Exception ex)
        {
            // Clean up on failure
            _tapStream?.Dispose();
            _tapHandle?.Dispose();
            _tapStream = null;
            _tapHandle = null;
            
            _logger.LogError(ex, "Failed to create Windows TAP interface");
            throw;
        }
    }

    private async Task GetTAPDriverInfo()
    {
        try
        {
            // Get TAP driver version
            IntPtr versionPtr = Marshal.AllocHGlobal(4);
            try
            {
                uint bytesReturned;
                bool success = DeviceIoControl(
                    _tapHandle!,
                    TAP_CONTROL_CODE(TAP_IOCTL_GET_VERSION, METHOD_BUFFERED),
                    IntPtr.Zero,
                    0,
                    versionPtr,
                    4,
                    out bytesReturned,
                    IntPtr.Zero);

                if (success && bytesReturned >= 4)
                {
                    uint version = (uint)Marshal.ReadInt32(versionPtr);
                    uint major = (version >> 16) & 0xFFFF;
                    uint minor = version & 0xFFFF;
                    _logger.LogInformation($"TAP driver version: {major}.{minor}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(versionPtr);
            }

            // Get TAP MTU
            IntPtr mtuPtr = Marshal.AllocHGlobal(4);
            try
            {
                uint bytesReturned;
                bool success = DeviceIoControl(
                    _tapHandle!,
                    TAP_CONTROL_CODE(TAP_IOCTL_GET_MTU, METHOD_BUFFERED),
                    IntPtr.Zero,
                    0,
                    mtuPtr,
                    4,
                    out bytesReturned,
                    IntPtr.Zero);

                if (success && bytesReturned >= 4)
                {
                    uint mtu = (uint)Marshal.ReadInt32(mtuPtr);
                    _logger.LogInformation($"TAP MTU: {mtu}");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(mtuPtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get TAP driver info (non-critical)");
        }

        await Task.CompletedTask;
    }

    private async Task ConfigureTAPForTUN(IPAddress localIP, IPAddress subnetMask)
    {
        try
        {
            // Configure point-to-point mode
            // This sets up the TAP adapter to work in TUN mode (Layer 3)
            var localBytes = localIP.GetAddressBytes();
            var remoteIP = GetRemoteIP(localIP, subnetMask);
            var remoteBytes = remoteIP.GetAddressBytes();

            // Create configuration buffer: [local_ip][remote_ip]
            IntPtr configPtr = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.Copy(localBytes, 0, configPtr, 4);
                Marshal.Copy(remoteBytes, 0, configPtr + 4, 4);

                uint bytesReturned;
                bool success = DeviceIoControl(
                    _tapHandle!,
                    TAP_CONTROL_CODE(TAP_IOCTL_CONFIG_POINT_TO_POINT, METHOD_BUFFERED),
                    configPtr,
                    8,
                    configPtr,
                    8,
                    out bytesReturned,
                    IntPtr.Zero);

                if (success)
                {
                    _logger.LogInformation($"TAP configured for point-to-point: {localIP} <-> {remoteIP}");
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger.LogWarning($"Point-to-point configuration failed (Win32 error {error}), trying TUN mode");
                    
                    // Try TUN mode configuration as fallback
                    await ConfigureTAPTunMode(localIP, subnetMask);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(configPtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TAP point-to-point configuration failed, trying alternative methods");
            await ConfigureTAPTunMode(localIP, subnetMask);
        }
    }

    private async Task ConfigureTAPTunMode(IPAddress localIP, IPAddress subnetMask)
    {
        try
        {
            // Alternative TUN mode configuration
            var localBytes = localIP.GetAddressBytes();
            var maskBytes = subnetMask.GetAddressBytes();

            // Create TUN configuration buffer: [local_ip][netmask]
            IntPtr tunConfigPtr = Marshal.AllocHGlobal(8);
            try
            {
                Marshal.Copy(localBytes, 0, tunConfigPtr, 4);
                Marshal.Copy(maskBytes, 0, tunConfigPtr + 4, 4);

                uint bytesReturned;
                bool success = DeviceIoControl(
                    _tapHandle!,
                    TAP_CONTROL_CODE(TAP_IOCTL_CONFIG_TUN, METHOD_BUFFERED),
                    tunConfigPtr,
                    8,
                    tunConfigPtr,
                    8,
                    out bytesReturned,
                    IntPtr.Zero);

                if (success)
                {
                    _logger.LogInformation($"TAP configured in TUN mode: {localIP}/{subnetMask}");
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    _logger.LogWarning($"TUN mode configuration failed (Win32 error {error}), will rely on interface configuration");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tunConfigPtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "TUN mode configuration failed, will rely on interface configuration");
        }

        await Task.CompletedTask;
    }

    private async Task SetTAPMediaStatus(bool connected)
    {
        try
        {
            IntPtr statusPtr = Marshal.AllocHGlobal(4);
            try
            {
                Marshal.WriteInt32(statusPtr, connected ? 1 : 0);
                uint bytesReturned;
                bool success = DeviceIoControl(
                    _tapHandle!,
                    TAP_CONTROL_CODE(TAP_IOCTL_SET_MEDIA_STATUS, METHOD_BUFFERED),
                    statusPtr,
                    4,
                    statusPtr,
                    4,
                    out bytesReturned,
                    IntPtr.Zero);

                if (success)
                {
                    _logger.LogInformation($"TAP media status set to: {(connected ? "connected" : "disconnected")}");
                }
                else
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new Win32Exception(error, "Failed to set TAP media status");
                }
            }
            finally
            {
                Marshal.FreeHGlobal(statusPtr);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set TAP media status");
            throw;
        }

        await Task.CompletedTask;
    }

    private IPAddress GetRemoteIP(IPAddress localIP, IPAddress subnetMask)
    {
        // Generate a remote IP for point-to-point configuration
        // Use the next IP in the subnet
        var localBytes = localIP.GetAddressBytes();
        var remoteBytes = new byte[4];
        Array.Copy(localBytes, remoteBytes, 4);
        
        // Increment the last octet
        remoteBytes[3] = (byte)((remoteBytes[3] + 1) % 256);
        
        return new IPAddress(remoteBytes);
    }

    private async Task CreateLinuxTUNAsync(IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            // Open TUN device
            _tunFd = open("/dev/net/tun", O_RDWR);
            if (_tunFd < 0)
            {
                throw new InvalidOperationException($"Failed to open /dev/net/tun: {GetLastErrorMessage()}");
            }

            // Configure TUN interface
            var ifr = new ifreq
            {
                ifr_name = _interfaceName,
                ifr_flags = IFF_TUN | IFF_NO_PI,
                padding = new byte[22]
            };

            IntPtr ifrPtr = Marshal.AllocHGlobal(Marshal.SizeOf(ifr));
            try
            {
                Marshal.StructureToPtr(ifr, ifrPtr, false);
                
                if (ioctl(_tunFd, TUNSETIFF, ifrPtr) < 0)
                {
                    throw new InvalidOperationException($"Failed to configure TUN interface: {GetLastErrorMessage()}");
                }

                // Get the actual interface name assigned by the kernel
                ifr = Marshal.PtrToStructure<ifreq>(ifrPtr);
                _interfaceName = ifr.ifr_name;
            }
            finally
            {
                Marshal.FreeHGlobal(ifrPtr);
            }

            // Create FileStream wrapper for the TUN file descriptor
            var safeHandle = new SafeFileHandle(new IntPtr(_tunFd), ownsHandle: false);
            _tunStream = new FileStream(safeHandle, FileAccess.ReadWrite, bufferSize: 1500, isAsync: true);

            // Configure the interface using ip commands
            await ConfigureLinuxInterface(virtualIP, subnetMask);

            _logger.LogInformation($"Linux TUN interface '{_interfaceName}' created and configured with IP {virtualIP}");
        }
        catch (Exception ex)
        {
            if (_tunFd >= 0)
            {
                close(_tunFd);
                _tunFd = -1;
            }
            _logger.LogError(ex, "Failed to create Linux TUN interface");
            throw;
        }
    }

    private async Task ConfigureLinuxInterface(IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            // Assign IP address
            var ipCmd = $"ip addr add {virtualIP}/{GetCidrFromMask(subnetMask)} dev {_interfaceName}";
            await ExecuteCommandAsync(ipCmd);

            // Bring interface up
            var upCmd = $"ip link set dev {_interfaceName} up";
            await ExecuteCommandAsync(upCmd);

            // Add route for VPN subnet (optional, depends on your routing needs)
            var routeCmd = $"ip route add {GetNetworkAddress(virtualIP, subnetMask)}/{GetCidrFromMask(subnetMask)} dev {_interfaceName}";
            try
            {
                await ExecuteCommandAsync(routeCmd);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add route (this may be normal if route already exists)");
            }

            _logger.LogInformation($"Linux TUN interface '{_interfaceName}' configured successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure Linux TUN interface");
            throw;
        }
    }

    private string GetLastErrorMessage()
    {
        int errno = Marshal.GetLastWin32Error();
        IntPtr strPtr = strerror(errno);
        return Marshal.PtrToStringAnsi(strPtr) ?? $"Unknown error ({errno})";
    }

    private IPAddress GetNetworkAddress(IPAddress ip, IPAddress mask)
    {
        byte[] ipBytes = ip.GetAddressBytes();
        byte[] maskBytes = mask.GetAddressBytes();
        byte[] networkBytes = new byte[ipBytes.Length];

        for (int i = 0; i < ipBytes.Length; i++)
        {
            networkBytes[i] = (byte)(ipBytes[i] & maskBytes[i]);
        }

        return new IPAddress(networkBytes);
    }

    private async Task ConfigureWindowsInterface(IPAddress virtualIP, IPAddress subnetMask)
    {
        try
        {
            // Find the TAP adapter name for netsh commands
            string? adapterName = GetTAPAdapterName();
            if (string.IsNullOrEmpty(adapterName))
            {
                throw new InvalidOperationException("Could not find TAP adapter name for configuration");
            }

            _logger.LogInformation($"Configuring TAP adapter '{adapterName}' with IP {virtualIP}/{subnetMask}");

            // First, try to enable the interface
            try
            {
                var enableCmd = $"netsh interface set interface name=\"{adapterName}\" admin=enabled";
                await ExecuteWindowsCommandAsync(enableCmd);
                _logger.LogDebug("TAP adapter enabled");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to enable TAP adapter (may already be enabled)");
            }

            // Configure IP address using netsh - try multiple methods
            bool configured = false;

            // Method 1: Static IP configuration
            try
            {
                var configCmd = $"netsh interface ip set address name=\"{adapterName}\" static {virtualIP} {subnetMask}";
                await ExecuteWindowsCommandAsync(configCmd);
                configured = true;
                _logger.LogInformation($"TAP adapter configured with static IP: {virtualIP}/{subnetMask}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Static IP configuration failed, trying alternative method");
            }

            // Method 2: Alternative configuration if static failed
            if (!configured)
            {
                try
                {
                    var deleteCmd = $"netsh interface ip delete address name=\"{adapterName}\" addr=all";
                    await ExecuteWindowsCommandAsync(deleteCmd);
                    
                    var addCmd = $"netsh interface ip add address name=\"{adapterName}\" addr={virtualIP} mask={subnetMask}";
                    await ExecuteWindowsCommandAsync(addCmd);
                    configured = true;
                    _logger.LogInformation($"TAP adapter configured with add address method: {virtualIP}/{subnetMask}");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Alternative IP configuration also failed");
                }
            }

            // Add route for the VPN subnet
            try
            {
                var networkAddress = GetNetworkAddress(virtualIP, subnetMask);
                var routeCmd = $"route add {networkAddress} mask {subnetMask} {virtualIP} if {GetInterfaceIndex(adapterName)}";
                await ExecuteWindowsCommandAsync(routeCmd);
                _logger.LogDebug($"Added route for {networkAddress}/{subnetMask}");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to add route (may not be critical)");
            }

            if (!configured)
            {
                throw new InvalidOperationException("Failed to configure TAP adapter IP address with any method");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to configure Windows TAP interface");
            throw;
        }
    }

    private int GetInterfaceIndex(string adapterName)
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name.Equals(adapterName, StringComparison.OrdinalIgnoreCase))
                {
                    var ipProps = ni.GetIPProperties();
                    return ipProps.GetIPv4Properties()?.Index ?? 0;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to get interface index");
        }
        return 0;
    }



    private string? GetTAPDeviceGuid()
    {
        try
        {
            // Try multiple methods to find TAP device GUID
            string? guid = null;

            // Method 1: Registry lookup (most reliable)
            guid = GetTAPDeviceGuidFromRegistry();
            if (!string.IsNullOrEmpty(guid))
            {
                _logger.LogDebug($"Found TAP GUID from registry: {guid}");
                return guid;
            }

            // Method 2: Network interfaces (fallback)
            guid = GetTAPDeviceGuidFromNetworkInterfaces();
            if (!string.IsNullOrEmpty(guid))
            {
                _logger.LogDebug($"Found TAP GUID from network interfaces: {guid}");
                return guid;
            }

            // Method 3: WMI lookup (last resort)
            guid = GetTAPDeviceGuidFromWMI();
            if (!string.IsNullOrEmpty(guid))
            {
                _logger.LogDebug($"Found TAP GUID from WMI: {guid}");
                return guid;
            }

            _logger.LogError("No TAP device found using any detection method");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find TAP device GUID");
            return null;
        }
    }

    private string? GetTAPDeviceGuidFromRegistry()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002BE10318}", 
                false); // Read-only access
            
            if (key != null)
            {
                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    try
                    {
                        using var subKey = key.OpenSubKey(subKeyName);
                        if (subKey == null) continue;

                        var componentId = subKey.GetValue("ComponentId") as string;
                        var driverDesc = subKey.GetValue("DriverDesc") as string;
                        
                        // Check for various TAP driver component IDs and descriptions
                        bool isTAPDevice = false;
                        
                        if (!string.IsNullOrEmpty(componentId))
                        {
                            // Modern TAP drivers
                            isTAPDevice = componentId.Equals("tap0901", StringComparison.OrdinalIgnoreCase) ||
                                         componentId.Equals("tap0801", StringComparison.OrdinalIgnoreCase) ||
                                         componentId.Equals("wintun", StringComparison.OrdinalIgnoreCase) ||
                                         componentId.Contains("tap", StringComparison.OrdinalIgnoreCase) ||
                                         componentId.Contains("tun", StringComparison.OrdinalIgnoreCase);
                        }
                        
                        if (!isTAPDevice && !string.IsNullOrEmpty(driverDesc))
                        {
                            // Check driver description for TAP indicators
                            isTAPDevice = driverDesc.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase) ||
                                         driverDesc.Contains("TAP Adapter", StringComparison.OrdinalIgnoreCase) ||
                                         driverDesc.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
                                         driverDesc.Contains("WinTun", StringComparison.OrdinalIgnoreCase);
                        }
                        
                        if (isTAPDevice)
                        {
                            var guid = subKey.GetValue("NetCfgInstanceId") as string;
                            if (!string.IsNullOrEmpty(guid))
                            {
                                _logger.LogDebug($"Found TAP device in registry: ComponentId={componentId}, DriverDesc={driverDesc}, GUID={guid}");
                                return guid;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, $"Error reading registry subkey {subKeyName}");
                        continue;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Registry TAP device lookup failed");
        }
        
        return null;
    }

    private string? GetTAPDeviceGuidFromWMI()
    {
        try
        {
            // Try multiple WMI queries for different TAP driver types
            string[] queries = {
                "SELECT * FROM Win32_NetworkAdapter WHERE Description LIKE '%TAP%'",
                "SELECT * FROM Win32_NetworkAdapter WHERE Description LIKE '%OpenVPN%'",
                "SELECT * FROM Win32_NetworkAdapter WHERE Description LIKE '%WinTun%'",
                "SELECT * FROM Win32_NetworkAdapter WHERE Name LIKE '%TAP%'"
            };

            foreach (string query in queries)
            {
                try
                {
                    using var searcher = new System.Management.ManagementObjectSearcher(query);
                    
                    foreach (System.Management.ManagementObject adapter in searcher.Get())
                    {
                        // Try different properties that might contain the GUID
                        var guid = adapter["GUID"]?.ToString() ?? 
                                  adapter["DeviceID"]?.ToString() ??
                                  adapter["PNPDeviceID"]?.ToString();
                        
                        if (!string.IsNullOrEmpty(guid))
                        {
                            // Extract GUID if it's embedded in a longer string
                            var guidMatch = System.Text.RegularExpressions.Regex.Match(guid, 
                                @"\{?([0-9A-Fa-f]{8}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{4}[-]?[0-9A-Fa-f]{12})\}?");
                            
                            if (guidMatch.Success)
                            {
                                var extractedGuid = guidMatch.Groups[1].Value;
                                _logger.LogDebug($"Found TAP device via WMI: {adapter["Description"]}, GUID: {extractedGuid}");
                                return extractedGuid;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, $"WMI query failed: {query}");
                    continue;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI TAP device lookup failed");
        }
        return null;
    }

    private string? GetTAPDeviceGuidFromNetworkInterfaces()
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                // Check for various TAP adapter descriptions
                bool isTAPInterface = ni.Description.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase) || 
                                     ni.Description.Contains("TAP Adapter", StringComparison.OrdinalIgnoreCase) ||
                                     ni.Description.Contains("TAP-Win32", StringComparison.OrdinalIgnoreCase) ||
                                     ni.Description.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
                                     ni.Description.Contains("WinTun", StringComparison.OrdinalIgnoreCase) ||
                                     ni.Name.Contains("TAP", StringComparison.OrdinalIgnoreCase);

                if (isTAPInterface)
                {
                    // Extract GUID from interface ID
                    var id = ni.Id;
                    if (Guid.TryParse(id, out _))
                    {
                        _logger.LogDebug($"Found TAP interface: {ni.Description}, Name: {ni.Name}, ID: {id}");
                        return id;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Network interface TAP lookup failed");
        }
        return null;
    }

    private string? GetTAPAdapterName()
    {
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                bool isTAPInterface = ni.Description.Contains("TAP-Windows", StringComparison.OrdinalIgnoreCase) || 
                                     ni.Description.Contains("TAP Adapter", StringComparison.OrdinalIgnoreCase) ||
                                     ni.Description.Contains("OpenVPN", StringComparison.OrdinalIgnoreCase) ||
                                     ni.Description.Contains("WinTun", StringComparison.OrdinalIgnoreCase) ||
                                     ni.Name.Contains("TAP", StringComparison.OrdinalIgnoreCase);

                if (isTAPInterface)
                {
                    _logger.LogDebug($"Found TAP adapter: {ni.Name} ({ni.Description})");
                    return ni.Name;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find TAP adapter name");
        }
        
        return null;
    }

    public async Task<byte[]> ReadPacketAsync(CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            throw new InvalidOperationException("Tunnel is not active");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _tapStream != null)
        {
            var buffer = new byte[1500]; // MTU size for Ethernet frames
            int bytesRead = await _tapStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            var packet = new byte[bytesRead];
            Array.Copy(buffer, packet, bytesRead);
            return packet;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _tunStream != null)
        {
            var buffer = new byte[1500]; // MTU size for IP packets
            int bytesRead = await _tunStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken);
            if (bytesRead > 0)
            {
                var packet = new byte[bytesRead];
                Array.Copy(buffer, packet, bytesRead);
                _logger.LogDebug($"Read {bytesRead} bytes from TUN interface");
                return packet;
            }
        }
        
        // Fallback to queue-based approach for testing or other platforms
        while (!cancellationToken.IsCancellationRequested && _isActive)
        {
            lock (_lockObject)
            {
                if (_incomingPackets.Count > 0)
                {
                    return _incomingPackets.Dequeue();
                }
            }

            await Task.Delay(1, cancellationToken);
        }

        throw new OperationCanceledException("Read operation was cancelled");
    }

    public async Task WritePacketAsync(byte[] packet, CancellationToken cancellationToken = default)
    {
        if (!_isActive)
            throw new InvalidOperationException("Tunnel is not active");

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && _tapStream != null)
        {
            await _tapStream.WriteAsync(packet, 0, packet.Length, cancellationToken);
            _logger.LogDebug($"Wrote {packet.Length} bytes to TAP interface");
            return;
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && _tunStream != null)
        {
            await _tunStream.WriteAsync(packet, 0, packet.Length, cancellationToken);
            _logger.LogDebug($"Wrote {packet.Length} bytes to TUN interface");
            return;
        }

        // Fallback to queue-based approach for testing
        lock (_lockObject)
        {
            _outgoingPackets.Enqueue(packet);
        }

        await Task.CompletedTask;
    }

    private async Task DestroyWindowsTunnelAsync()
    {
        try
        {
            // Set media status to disconnected before cleanup
            if (_tapHandle != null && !_tapHandle.IsInvalid)
            {
                try
                {
                    await SetTAPMediaStatus(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to set TAP media status to disconnected during cleanup");
                }
            }

            // Dispose streams and handles
            _tapStream?.Dispose();
            _tapStream = null;
            
            _tapHandle?.Dispose();
            _tapHandle = null;

            // Optional: Reset adapter configuration
            try
            {
                string? adapterName = GetTAPAdapterName();
                if (!string.IsNullOrEmpty(adapterName))
                {
                    var resetCmd = $"netsh interface ip set address name=\"{adapterName}\" dhcp";
                    await ExecuteWindowsCommandAsync(resetCmd);
                    _logger.LogDebug($"Reset TAP adapter '{adapterName}' to DHCP");
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "TAP adapter reset failed (this is normal)");
            }

            _logger.LogInformation("Windows TAP interface cleanup completed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during Windows TAP cleanup");
        }

        await Task.CompletedTask;
    }

    private async Task DestroyLinuxTUNAsync()
    {
        try
        {
            // Close the TUN stream and file descriptor
            _tunStream?.Dispose();
            _tunStream = null;

            if (_tunFd >= 0)
            {
                close(_tunFd);
                _tunFd = -1;
            }

            // The TUN interface will be automatically removed when the file descriptor is closed
            // But we can also explicitly remove it if needed
            try
            {
                var deleteCmd = $"ip link delete {_interfaceName}";
                await ExecuteCommandAsync(deleteCmd);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Interface cleanup command failed (this is normal)");
            }

            _logger.LogInformation($"Linux TUN interface '{_interfaceName}' destroyed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, $"Failed to destroy Linux TUN interface '{_interfaceName}'");
        }
    }

    private async Task ExecuteCommandAsync(string command)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo.FileName = "/bin/bash";
        process.StartInfo.Arguments = $"-c \"{command}\"";
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;

        process.Start();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync();
            throw new InvalidOperationException($"Command failed: {command}. Error: {error}");
        }
    }

    private async Task ExecuteWindowsCommandAsync(string command)
    {
        try
        {
            _logger.LogDebug($"Executing Windows command: {command}");
            
            using var process = new System.Diagnostics.Process();
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = $"/c {command}";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.StartInfo.RedirectStandardError = true;
            process.StartInfo.CreateNoWindow = true;
            process.StartInfo.Verb = "runas"; // Request admin privileges

            process.Start();
            
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            
            await process.WaitForExitAsync();
            
            var output = await outputTask;
            var error = await errorTask;

            if (process.ExitCode != 0)
            {
                var errorMessage = !string.IsNullOrEmpty(error) ? error : "Unknown error";
                _logger.LogWarning($"Command failed with exit code {process.ExitCode}: {command}. Error: {errorMessage}");
                throw new InvalidOperationException($"Command failed: {command}. Exit code: {process.ExitCode}. Error: {errorMessage}");
            }
            else
            {
                if (!string.IsNullOrEmpty(output))
                {
                    _logger.LogDebug($"Command output: {output.Trim()}");
                }
                _logger.LogDebug($"Command executed successfully: {command}");
            }
        }
        catch (Exception ex) when (!(ex is InvalidOperationException))
        {
            _logger.LogError(ex, $"Failed to execute Windows command: {command}");
            throw new InvalidOperationException($"Failed to execute command: {command}. Error: {ex.Message}", ex);
        }
    }

    // ... (rest of the methods remain the same)

    public async Task DestroyTunnelAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _isActive = false;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                await DestroyWindowsTunnelAsync();
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                await DestroyLinuxTUNAsync();
            }

            _logger.LogInformation($"VPN TAP interface '{_interfaceName}' destroyed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to destroy VPN TAP interface '{_interfaceName}'");
            throw;
        }
    }

    private static int GetCidrFromMask(IPAddress mask)
    {
        var bytes = mask.GetAddressBytes();
        var cidr = 0;
        
        foreach (var b in bytes)
        {
            cidr += CountBits(b);
        }
        
        return cidr;
    }

    private static int CountBits(byte value)
    {
        var count = 0;
        while (value != 0)
        {
            count++;
            value &= (byte)(value - 1);
        }
        return count;
    }

    public void Dispose()
    {
        _cancellationTokenSource?.Cancel();
        _isActive = false;
        
        // Clean up Windows resources
        _tapStream?.Dispose();
        _tapHandle?.Dispose();
        
        // Clean up Linux resources
        _tunStream?.Dispose();
        if (_tunFd >= 0)
        {
            close(_tunFd);
            _tunFd = -1;
        }
        
        _cancellationTokenSource?.Dispose();
        GC.SuppressFinalize(this);
    }
}
