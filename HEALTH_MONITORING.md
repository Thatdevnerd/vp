# VPN Client Health Monitoring System

## Overview

The VPN application includes a comprehensive health monitoring system that tracks the connectivity and performance of VPN clients in real-time. This system ensures reliable connections and automatically handles disconnected or unresponsive clients.

## Features

### 🔍 Real-time Health Monitoring
- **Heartbeat Mechanism**: Clients send heartbeat packets every 30 seconds
- **Server Health Checks**: Server monitors client responsiveness and network latency
- **Automatic Timeout Detection**: Clients are marked as unhealthy after missing 3 heartbeats or being inactive for 90+ seconds
- **Round-Trip Time (RTT) Tracking**: Exponential moving average calculation for network latency

### 📊 Health Metrics
- **Last Heartbeat**: Timestamp of the most recent heartbeat received
- **Missed Heartbeats**: Counter of consecutive missed heartbeat packets
- **Average RTT**: Network latency with exponential smoothing (α=0.2)
- **Health Status**: Boolean indicator based on heartbeat freshness and missed count

### 🔄 Automatic Management
- **Client Registration**: Automatic enrollment in health monitoring upon connection
- **Timeout Cleanup**: Automatic removal of unresponsive clients and resource cleanup
- **Event-Driven Architecture**: Real-time notifications for health status changes

## Architecture

### Core Components

#### 1. VPNClientHealthMonitor
```csharp
public class VPNClientHealthMonitor : IDisposable
{
    // Configurable parameters
    private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);
    private readonly TimeSpan _clientTimeout = TimeSpan.FromSeconds(90);
    private readonly int _maxMissedHeartbeats = 3;
    
    // Events
    public event EventHandler<VPNClientInfo>? ClientTimedOut;
    public event EventHandler<VPNClientInfo>? ClientHealthChanged;
}
```

#### 2. Enhanced VPNClientInfo
```csharp
public class VPNClientInfo
{
    // Health tracking properties
    public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;
    public long AverageRoundTripTime { get; set; }
    public int MissedHeartbeats { get; set; }
    
    // Computed health status
    public bool IsHealthy => (DateTime.UtcNow - LastHeartbeat).TotalSeconds < 60 
                          && MissedHeartbeats < 3;
}
```

#### 3. Heartbeat Packet Types
```csharp
public class VPNHeartbeatPacket : VPNPacket
{
    public DateTime ClientTime { get; set; }
    public long ClientUptime { get; set; }
    public int PendingPackets { get; set; }
    public string ClientStatus { get; set; } = "Active";
}

public class VPNHeartbeatResponsePacket : VPNPacket
{
    public DateTime ServerTime { get; set; }
    public DateTime ClientTime { get; set; }
    public long RoundTripTime { get; set; }
    public string ServerStatus { get; set; } = "Active";
    public int ConnectedClients { get; set; }
}
```

## Configuration

### Health Monitor Settings
```csharp
// Default configuration
private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);    // Client heartbeat frequency
private readonly TimeSpan _clientTimeout = TimeSpan.FromSeconds(90);        // Client timeout threshold
private readonly int _maxMissedHeartbeats = 3;                              // Max missed before unhealthy
```

### Health Determination Logic
A client is considered **healthy** when:
- Last heartbeat received within 60 seconds
- Missed heartbeats count < 3

A client is considered **unhealthy** when:
- Last heartbeat older than 60 seconds, OR
- Missed heartbeats count ≥ 3

## Usage Examples

### Server-Side Integration
```csharp
// Initialize health monitor
var healthMonitor = new VPNClientHealthMonitor(logger, transport);

// Subscribe to events
healthMonitor.ClientTimedOut += (sender, client) =>
{
    logger.LogWarning($"Client {client.ClientId} timed out");
    // Cleanup resources
};

healthMonitor.ClientHealthChanged += (sender, client) =>
{
    logger.LogInformation($"Client {client.ClientId} health: {(client.IsHealthy ? "Healthy" : "Unhealthy")}");
};

// Register clients
healthMonitor.RegisterClient(clientInfo);

// Process heartbeats
var response = await healthMonitor.ProcessHeartbeatAsync(clientId, heartbeatPacket);
```

### Client-Side Implementation
```csharp
// Automatic heartbeat timer
private Timer? _heartbeatTimer;
private readonly TimeSpan _heartbeatInterval = TimeSpan.FromSeconds(30);

// Start heartbeat when connected
_heartbeatTimer = new Timer(SendHeartbeat, null, _heartbeatInterval, _heartbeatInterval);

// Send heartbeat
private async void SendHeartbeat(object? state)
{
    var heartbeat = new VPNHeartbeatPacket
    {
        ClientTime = DateTime.UtcNow,
        ClientUptime = (long)(DateTime.UtcNow - _startTime).TotalSeconds,
        ClientStatus = "Active"
    };
    
    await SendPacketAsync(heartbeat);
}
```

### Health Status Queries
```csharp
// Get healthy clients
var healthyClients = healthMonitor.GetHealthyClients();

// Get unhealthy clients
var unhealthyClients = healthMonitor.GetUnhealthyClients();

// Check specific client
var client = healthMonitor.GetClientInfo(clientId);
if (client?.IsHealthy == true)
{
    Console.WriteLine($"Client {clientId} is healthy (RTT: {client.AverageRoundTripTime}ms)");
}
```

## Monitoring Dashboard

### Health Summary
```
📊 VPN Health Status
===================
🟢 Healthy Clients: 15
🟡 Unhealthy Clients: 2
🔴 Timed Out: 1

📈 Performance Metrics
=====================
Average RTT: 45ms
Max RTT: 120ms
Min RTT: 12ms
```

### Client Details
```
Client ID: client-001
Status: 🟢 Healthy
RTT: 34ms (avg)
Last Heartbeat: 15 seconds ago
Missed Heartbeats: 0
Connected: 2h 15m
```

## Testing

### Unit Tests
The system includes comprehensive unit tests covering:
- Client registration/unregistration
- Heartbeat processing and RTT calculation
- Health status determination
- Event firing for timeouts and health changes
- Exponential moving average calculations

### Demo Application
Run the health monitoring demo:
```bash
cd VPNHealthMonitorDemo
dotnet run
```

The demo simulates multiple clients with different behaviors:
- 70% normal heartbeats
- 15% delayed heartbeats
- 10% intermittent issues
- 5% client disconnections

## Performance Considerations

### Memory Usage
- Minimal overhead per client (~200 bytes for health tracking)
- Automatic cleanup of disconnected clients
- Efficient concurrent collections for thread safety

### Network Overhead
- Heartbeat packets: ~100 bytes every 30 seconds per client
- Negligible impact on VPN throughput
- Configurable intervals for different network conditions

### CPU Usage
- Timer-based health checks: ~1ms per client every 30 seconds
- Event-driven architecture minimizes polling overhead
- Efficient RTT calculations using exponential moving average

## Troubleshooting

### Common Issues

#### High RTT Values
```
Possible causes:
- Network congestion
- Server overload
- Client network issues

Solutions:
- Check network connectivity
- Monitor server resources
- Adjust heartbeat intervals
```

#### Frequent Timeouts
```
Possible causes:
- Aggressive timeout settings
- Unstable network connections
- Client application issues

Solutions:
- Increase timeout thresholds
- Improve network stability
- Debug client heartbeat logic
```

#### Memory Leaks
```
Possible causes:
- Clients not properly unregistered
- Event handlers not disposed
- Timer resources not cleaned up

Solutions:
- Ensure proper disposal patterns
- Monitor client registration/unregistration
- Use using statements for disposable resources
```

## Future Enhancements

### Planned Features
- **Adaptive Heartbeat Intervals**: Dynamic adjustment based on network conditions
- **Health History**: Long-term storage of health metrics for analysis
- **Predictive Monitoring**: ML-based prediction of client disconnections
- **Custom Health Policies**: Configurable health determination rules
- **Integration APIs**: REST endpoints for external monitoring systems

### Configuration Options
- **Heartbeat Frequency**: Per-client or global settings
- **Timeout Thresholds**: Different timeouts for different client types
- **RTT Smoothing**: Configurable exponential moving average parameters
- **Event Filtering**: Selective event subscriptions based on criteria

## API Reference

### VPNClientHealthMonitor Methods
```csharp
// Client management
void RegisterClient(VPNClientInfo client)
void UnregisterClient(string clientId)

// Health monitoring
void UpdateClientHeartbeat(string clientId, VPNHeartbeatPacket heartbeat)
Task<VPNHeartbeatResponsePacket> ProcessHeartbeatAsync(string clientId, VPNHeartbeatPacket heartbeat)

// Health queries
VPNClientInfo? GetClientInfo(string clientId)
IEnumerable<VPNClientInfo> GetHealthyClients()
IEnumerable<VPNClientInfo> GetUnhealthyClients()

// Events
event EventHandler<VPNClientInfo>? ClientTimedOut
event EventHandler<VPNClientInfo>? ClientHealthChanged
```

### Health Status Properties
```csharp
// VPNClientInfo health properties
DateTime LastHeartbeat          // Last heartbeat timestamp
long AverageRoundTripTime      // Network latency in milliseconds
int MissedHeartbeats           // Consecutive missed heartbeats
bool IsHealthy                 // Computed health status
```

This health monitoring system provides robust, real-time visibility into VPN client connectivity and performance, enabling proactive management and improved user experience.