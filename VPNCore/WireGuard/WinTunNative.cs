using System.Runtime.InteropServices;

namespace VPNCore.WireGuard;

/// <summary>
/// Native WinTun driver interface for Windows WireGuard implementation
/// WinTun is the modern replacement for TAP-Windows driver
/// </summary>
public static class WinTunNative
{
    private const string WINTUN_DLL = "wintun.dll";

    // WinTun adapter management
    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern IntPtr WintunCreateAdapter(
        [MarshalAs(UnmanagedType.LPWStr)] string name,
        [MarshalAs(UnmanagedType.LPWStr)] string tunnelType,
        ref Guid requestedGUID);

    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern bool WintunCloseAdapter(IntPtr adapter);

    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern bool WintunDeleteDriver();

    // WinTun session management
    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern IntPtr WintunStartSession(IntPtr adapter, uint capacity);

    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern void WintunEndSession(IntPtr session);

    // Packet I/O
    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern IntPtr WintunReceivePacket(IntPtr session, out uint packetSize);

    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern void WintunReleaseReceivePacket(IntPtr session, IntPtr packet);

    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern IntPtr WintunAllocateSendPacket(IntPtr session, uint packetSize);

    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern void WintunSendPacket(IntPtr session, IntPtr packet);

    // Event handling
    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern IntPtr WintunGetReadWaitEvent(IntPtr session);

    // Adapter enumeration
    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern bool WintunEnumAdapters(
        [MarshalAs(UnmanagedType.LPWStr)] string tunnelType,
        IntPtr callback,
        IntPtr param);

    [DllImport(WINTUN_DLL, CallingConvention = CallingConvention.StdCall, SetLastError = true)]
    private static extern IntPtr WintunOpenAdapter(
        [MarshalAs(UnmanagedType.LPWStr)] string name);

    // High-level wrapper methods
    private static readonly Dictionary<IntPtr, IntPtr> _sessions = new();
    private static readonly object _lock = new();

    /// <summary>
    /// Create a new WinTun session for packet I/O
    /// </summary>
    public static IntPtr CreateSession(string adapterName)
    {
        try
        {
            // Try to open existing adapter first
            var adapter = WintunOpenAdapter(adapterName);
            
            if (adapter == IntPtr.Zero)
            {
                // Create new adapter
                var guid = Guid.NewGuid();
                adapter = WintunCreateAdapter(adapterName, "WireGuard", ref guid);
                
                if (adapter == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"Failed to create WinTun adapter: Win32 error {error}");
                }
            }

            // Start session with 2MB ring buffer
            var session = WintunStartSession(adapter, 0x200000);
            if (session == IntPtr.Zero)
            {
                WintunCloseAdapter(adapter);
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Failed to start WinTun session: Win32 error {error}");
            }

            lock (_lock)
            {
                _sessions[session] = adapter;
            }

            return session;
        }
        catch (DllNotFoundException)
        {
            throw new InvalidOperationException("WinTun driver not found. Please install WinTun from https://www.wintun.net/");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to create WinTun session: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Close a WinTun session
    /// </summary>
    public static void CloseSession(IntPtr session)
    {
        if (session == IntPtr.Zero) return;

        try
        {
            IntPtr adapter;
            lock (_lock)
            {
                if (!_sessions.TryGetValue(session, out adapter))
                    return;
                
                _sessions.Remove(session);
            }

            WintunEndSession(session);
            
            if (adapter != IntPtr.Zero)
            {
                WintunCloseAdapter(adapter);
            }
        }
        catch (Exception)
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Read a packet from the WinTun session
    /// </summary>
    public static byte[]? ReadPacket(IntPtr session)
    {
        if (session == IntPtr.Zero)
            return null;

        try
        {
            var packetPtr = WintunReceivePacket(session, out uint packetSize);
            if (packetPtr == IntPtr.Zero)
            {
                // No packet available
                return null;
            }

            try
            {
                var packet = new byte[packetSize];
                Marshal.Copy(packetPtr, packet, 0, (int)packetSize);
                return packet;
            }
            finally
            {
                WintunReleaseReceivePacket(session, packetPtr);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Write a packet to the WinTun session
    /// </summary>
    public static bool WritePacket(IntPtr session, byte[] packet)
    {
        if (session == IntPtr.Zero || packet == null || packet.Length == 0)
            return false;

        try
        {
            var packetPtr = WintunAllocateSendPacket(session, (uint)packet.Length);
            if (packetPtr == IntPtr.Zero)
            {
                return false;
            }

            Marshal.Copy(packet, 0, packetPtr, packet.Length);
            WintunSendPacket(session, packetPtr);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Get the read wait event handle for async operations
    /// </summary>
    public static IntPtr GetReadWaitEvent(IntPtr session)
    {
        if (session == IntPtr.Zero)
            return IntPtr.Zero;

        try
        {
            return WintunGetReadWaitEvent(session);
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Check if WinTun driver is available
    /// </summary>
    public static bool IsWinTunAvailable()
    {
        try
        {
            // Try to load the DLL
            var testGuid = Guid.NewGuid();
            var adapter = WintunCreateAdapter("test", "test", ref testGuid);
            if (adapter != IntPtr.Zero)
            {
                WintunCloseAdapter(adapter);
            }
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (Exception)
        {
            // Other errors might indicate the driver is present but something else is wrong
            return true;
        }
    }

    /// <summary>
    /// Get WinTun driver version information
    /// </summary>
    public static string GetWinTunVersion()
    {
        try
        {
            // WinTun doesn't have a direct version API, so we'll return a placeholder
            return IsWinTunAvailable() ? "Available" : "Not Available";
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// Cleanup all WinTun resources
    /// </summary>
    public static void Cleanup()
    {
        lock (_lock)
        {
            foreach (var kvp in _sessions.ToList())
            {
                CloseSession(kvp.Key);
            }
            _sessions.Clear();
        }
    }
}

/// <summary>
/// WinTun adapter information
/// </summary>
public class WinTunAdapterInfo
{
    public string Name { get; set; } = string.Empty;
    public Guid GUID { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool IsActive { get; set; }
}

/// <summary>
/// WinTun session statistics
/// </summary>
public class WinTunSessionStats
{
    public long PacketsReceived { get; set; }
    public long PacketsSent { get; set; }
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public DateTime SessionStartTime { get; set; }
    public TimeSpan SessionDuration => DateTime.UtcNow - SessionStartTime;
}