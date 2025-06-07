using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using VPNCore.Models;

namespace VPNCore.Interfaces;

public interface IVPNServer
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task<bool> IsRunningAsync();
    Task<VPNServerInfo> GetServerInfoAsync();
    Task<IEnumerable<VPNClientInfo>> GetConnectedClientsAsync();
    Task DisconnectClientAsync(string clientId);
    event EventHandler<VPNClientInfo>? ClientConnected;
    event EventHandler<VPNClientInfo>? ClientDisconnected;
    event EventHandler<string>? LogMessage;
}

public interface IVPNClient
{
    Task ConnectAsync(VPNConfiguration configuration, CancellationToken cancellationToken = default);
    Task DisconnectAsync(CancellationToken cancellationToken = default);
    Task<VPNConnectionStatus> GetStatusAsync();
    Task<VPNClientInfo> GetClientInfoAsync();
    event EventHandler<VPNConnectionStatus>? StatusChanged;
    event EventHandler<string>? LogMessage;
}

public interface IVPNTunnel : IDisposable
{
    Task CreateTunnelAsync(string interfaceName, IPAddress virtualIP, IPAddress subnetMask);
    Task DestroyTunnelAsync();
    Task<byte[]> ReadPacketAsync(CancellationToken cancellationToken = default);
    Task WritePacketAsync(byte[] packet, CancellationToken cancellationToken = default);
    bool IsActive { get; }
}

public interface IVPNCryptography
{
    byte[] Encrypt(byte[] data, byte[] key, byte[] iv);
    byte[] Decrypt(byte[] encryptedData, byte[] key, byte[] iv);
    (byte[] publicKey, byte[] privateKey) GenerateKeyPair();
    byte[] ComputeSharedSecret(byte[] privateKey, byte[] publicKey);
    byte[] GenerateRandomBytes(int length);
    bool VerifySignature(byte[] data, byte[] signature, byte[] publicKey);
    byte[] SignData(byte[] data, byte[] privateKey);
}