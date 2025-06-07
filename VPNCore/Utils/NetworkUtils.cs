using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace VPNCore.Utils;

public static class NetworkUtils
{
    public static bool IsPortAvailable(int port)
    {
        try
        {
            using var tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start();
            tcpListener.Stop();
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static IPAddress GetLocalIPAddress()
    {
        try
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            foreach (var ip in host.AddressList)
            {
                if (ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                {
                    return ip;
                }
            }
        }
        catch
        {
            // Fall back to loopback
        }
        
        return IPAddress.Loopback;
    }

    public static bool IsValidIPAddress(string ipString)
    {
        return IPAddress.TryParse(ipString, out _);
    }

    public static bool IsNetworkAvailable()
    {
        return NetworkInterface.GetIsNetworkAvailable();
    }

    public static IEnumerable<NetworkInterface> GetActiveNetworkInterfaces()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(ni => ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
    }

    public static bool CanReachHost(string hostname, int port, int timeoutMs = 5000)
    {
        try
        {
            using var client = new TcpClient();
            var result = client.BeginConnect(hostname, port, null, null);
            var success = result.AsyncWaitHandle.WaitOne(timeoutMs);
            
            if (success)
            {
                client.EndConnect(result);
                return true;
            }
            
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static IPAddress GetSubnetMask(IPAddress ip)
    {
        // Simple subnet mask detection based on IP class
        var bytes = ip.GetAddressBytes();
        
        if (bytes[0] < 128) // Class A
            return IPAddress.Parse("255.0.0.0");
        else if (bytes[0] < 192) // Class B
            return IPAddress.Parse("255.255.0.0");
        else // Class C
            return IPAddress.Parse("255.255.255.0");
    }

    public static bool IsInSameSubnet(IPAddress ip1, IPAddress ip2, IPAddress subnetMask)
    {
        var ip1Bytes = ip1.GetAddressBytes();
        var ip2Bytes = ip2.GetAddressBytes();
        var maskBytes = subnetMask.GetAddressBytes();

        for (int i = 0; i < ip1Bytes.Length; i++)
        {
            if ((ip1Bytes[i] & maskBytes[i]) != (ip2Bytes[i] & maskBytes[i]))
                return false;
        }

        return true;
    }
}