using System.Net;

namespace VPNCore.Models;

public class VPNConfiguration
{
    public string ServerAddress { get; set; } = string.Empty;
    public int ServerPort { get; set; } = 1194;
    public string Protocol { get; set; } = "UDP";
    public string EncryptionAlgorithm { get; set; } = "AES-256-GCM";
    public string AuthenticationMethod { get; set; } = "RSA-2048";
    public IPAddress VirtualNetworkAddress { get; set; } = IPAddress.Parse("10.8.0.0");
    public IPAddress VirtualNetworkMask { get; set; } = IPAddress.Parse("255.255.255.0");
    public List<IPAddress> DNSServers { get; set; } = new();
    public bool RedirectGateway { get; set; } = true;
    public int KeepAliveInterval { get; set; } = 10;
    public int ConnectionTimeout { get; set; } = 30;
    public string CertificatePath { get; set; } = string.Empty;
    public string PrivateKeyPath { get; set; } = string.Empty;
    public string CACertificatePath { get; set; } = string.Empty;
    public bool CompressData { get; set; } = true;
    public VPNLogLevel LogLevel { get; set; } = VPNLogLevel.Information;
}

public enum VPNLogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}