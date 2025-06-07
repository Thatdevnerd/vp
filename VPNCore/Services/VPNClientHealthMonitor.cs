using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VPNCore.Models;
using VPNCore.Interfaces;
using VPNCore.Networking;

namespace VPNCore.Services;

public class VPNClientHealthMonitor : IDisposable
{
    private readonly ILogger<VPNClientHealthMonitor> _logger;
    private readonly ConcurrentDictionary<string, VPNClientInfo> _clients;
    private readonly Timer _healthCheckTimer;
    private readonly Timer _heartbeatTimer;
    private readonly IVPNTransport _transport;
    
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _healthCheckInterval = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _clientTimeout = TimeSpan.FromSeconds(90);
    private readonly int _maxMissedHeartbeats = 3;
    
    public event EventHandler<VPNClientInfo>? ClientTimedOut;
    public event EventHandler<VPNClientInfo>? ClientHealthChanged;
    
    public VPNClientHealthMonitor(
        ILogger<VPNClientHealthMonitor> logger,
        IVPNTransport transport)
    {
        _logger = logger;
        _transport = transport;
        _clients = new ConcurrentDictionary<string, VPNClientInfo>();
        
        _healthCheckTimer = new Timer(PerformHealthCheck, null, _healthCheckInterval, _healthCheckInterval);
        _heartbeatTimer = new Timer(SendHeartbeats, null, _heartbeatInterval, _heartbeatInterval);
        
        _logger.LogInformation("VPN Client Health Monitor started");
    }
    
    public void RegisterClient(VPNClientInfo client)
    {
        _clients.TryAdd(client.ClientId, client);
        client.LastHeartbeat = DateTime.UtcNow;
        client.MissedHeartbeats = 0;
        
        _logger.LogInformation($"Client {client.ClientId} registered for health monitoring");
    }
    
    public void UnregisterClient(string clientId)
    {
        if (_clients.TryRemove(clientId, out var client))
        {
            _logger.LogInformation($"Client {clientId} unregistered from health monitoring");
        }
    }
    
    public void UpdateClientHeartbeat(string clientId, VPNHeartbeatPacket heartbeat)
    {
        if (_clients.TryGetValue(clientId, out var client))
        {
            var previousHealth = client.IsHealthy;
            
            client.LastHeartbeat = DateTime.UtcNow;
            client.LastActivity = DateTime.UtcNow;
            client.MissedHeartbeats = 0;
            
            // Calculate round trip time
            var rtt = (DateTime.UtcNow - heartbeat.ClientTime).TotalMilliseconds;
            if (client.AverageRoundTripTime == 0)
            {
                client.AverageRoundTripTime = (long)rtt;
            }
            else
            {
                // Exponential moving average
                client.AverageRoundTripTime = (long)(0.8 * client.AverageRoundTripTime + 0.2 * rtt);
            }
            
            if (previousHealth != client.IsHealthy)
            {
                ClientHealthChanged?.Invoke(this, client);
                _logger.LogInformation($"Client {clientId} health changed to {(client.IsHealthy ? "healthy" : "unhealthy")}");
            }
            
            _logger.LogDebug($"Heartbeat received from client {clientId}, RTT: {rtt:F2}ms");
        }
    }
    
    public async Task<VPNHeartbeatResponsePacket> ProcessHeartbeatAsync(string clientId, VPNHeartbeatPacket heartbeat)
    {
        UpdateClientHeartbeat(clientId, heartbeat);
        
        var response = new VPNHeartbeatResponsePacket
        {
            Type = VPNPacketType.HeartbeatResponse,
            SessionId = heartbeat.SessionId,
            ServerTime = DateTime.UtcNow,
            ClientTime = heartbeat.ClientTime,
            RoundTripTime = (DateTime.UtcNow - heartbeat.ClientTime).Ticks,
            ServerStatus = "Active",
            ConnectedClients = _clients.Count
        };
        
        return await Task.FromResult(response);
    }
    
    public IEnumerable<VPNClientInfo> GetHealthyClients()
    {
        return _clients.Values.Where(c => c.IsHealthy);
    }
    
    public IEnumerable<VPNClientInfo> GetUnhealthyClients()
    {
        return _clients.Values.Where(c => !c.IsHealthy);
    }
    
    public VPNClientInfo? GetClientInfo(string clientId)
    {
        _clients.TryGetValue(clientId, out var client);
        return client;
    }
    
    private async void PerformHealthCheck(object? state)
    {
        try
        {
            var now = DateTime.UtcNow;
            var clientsToRemove = new List<string>();
            
            foreach (var kvp in _clients)
            {
                var client = kvp.Value;
                var timeSinceLastHeartbeat = now - client.LastHeartbeat;
                
                if (timeSinceLastHeartbeat > _clientTimeout)
                {
                    _logger.LogWarning($"Client {client.ClientId} timed out (last heartbeat: {timeSinceLastHeartbeat.TotalSeconds:F1}s ago)");
                    
                    client.Status = VPNConnectionStatus.Disconnected;
                    ClientTimedOut?.Invoke(this, client);
                    clientsToRemove.Add(client.ClientId);
                }
                else if (timeSinceLastHeartbeat > _heartbeatInterval)
                {
                    client.MissedHeartbeats++;
                    
                    if (client.MissedHeartbeats >= _maxMissedHeartbeats)
                    {
                        _logger.LogWarning($"Client {client.ClientId} missed {client.MissedHeartbeats} heartbeats");
                        
                        if (client.Status == VPNConnectionStatus.Connected)
                        {
                            client.Status = VPNConnectionStatus.Error;
                            ClientHealthChanged?.Invoke(this, client);
                        }
                    }
                }
            }
            
            // Remove timed out clients
            foreach (var clientId in clientsToRemove)
            {
                UnregisterClient(clientId);
            }
            
            if (clientsToRemove.Count > 0)
            {
                _logger.LogInformation($"Removed {clientsToRemove.Count} timed out clients");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check");
        }
    }
    
    private async void SendHeartbeats(object? state)
    {
        try
        {
            var heartbeatTasks = new List<Task>();
            
            foreach (var client in _clients.Values.Where(c => c.Status == VPNConnectionStatus.Connected))
            {
                var heartbeatPacket = new VPNHeartbeatPacket
                {
                    Type = VPNPacketType.Heartbeat,
                    SessionId = client.ClientId,
                    Timestamp = DateTime.UtcNow
                };
                
                // Send heartbeat to client (this would be implemented in the transport layer)
                heartbeatTasks.Add(SendHeartbeatToClientAsync(client, heartbeatPacket));
            }
            
            if (heartbeatTasks.Count > 0)
            {
                await Task.WhenAll(heartbeatTasks);
                _logger.LogDebug($"Sent heartbeats to {heartbeatTasks.Count} clients");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending heartbeats");
        }
    }
    
    private async Task SendHeartbeatToClientAsync(VPNClientInfo client, VPNHeartbeatPacket heartbeat)
    {
        try
        {
            // This would be implemented by the transport layer
            // For now, we'll just log it
            _logger.LogDebug($"Sending heartbeat to client {client.ClientId}");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Failed to send heartbeat to client {client.ClientId}");
            client.MissedHeartbeats++;
        }
    }
    
    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        _heartbeatTimer?.Dispose();
        _logger.LogInformation("VPN Client Health Monitor disposed");
        GC.SuppressFinalize(this);
    }
}