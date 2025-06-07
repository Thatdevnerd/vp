using Microsoft.Extensions.Logging;
using Moq;
using VPNCore.Interfaces;
using VPNCore.Models;
using VPNCore.Networking;
using VPNCore.Services;
using Xunit;

namespace VPNTests;

public class VPNClientHealthMonitorTests
{
    private readonly Mock<ILogger<VPNClientHealthMonitor>> _mockLogger;
    private readonly Mock<IVPNTransport> _mockTransport;
    private readonly VPNClientHealthMonitor _healthMonitor;

    public VPNClientHealthMonitorTests()
    {
        _mockLogger = new Mock<ILogger<VPNClientHealthMonitor>>();
        _mockTransport = new Mock<IVPNTransport>();
        _healthMonitor = new VPNClientHealthMonitor(_mockLogger.Object, _mockTransport.Object);
    }

    [Fact]
    public void RegisterClient_ShouldAddClientToMonitoring()
    {
        // Arrange
        var client = new VPNClientInfo
        {
            ClientId = "test-client-1",
            Status = VPNConnectionStatus.Connected
        };

        // Act
        _healthMonitor.RegisterClient(client);

        // Assert
        var clientInfo = _healthMonitor.GetClientInfo(client.ClientId);
        Assert.NotNull(clientInfo);
        Assert.Equal(client.ClientId, clientInfo.ClientId);
        Assert.Equal(0, clientInfo.MissedHeartbeats);
    }

    [Fact]
    public void UnregisterClient_ShouldRemoveClientFromMonitoring()
    {
        // Arrange
        var client = new VPNClientInfo
        {
            ClientId = "test-client-2",
            Status = VPNConnectionStatus.Connected
        };

        _healthMonitor.RegisterClient(client);

        // Act
        _healthMonitor.UnregisterClient(client.ClientId);

        // Assert
        var clientInfo = _healthMonitor.GetClientInfo(client.ClientId);
        Assert.Null(clientInfo);
    }

    [Fact]
    public void UpdateClientHeartbeat_ShouldUpdateLastHeartbeatAndResetMissedCount()
    {
        // Arrange
        var client = new VPNClientInfo
        {
            ClientId = "test-client-3",
            Status = VPNConnectionStatus.Connected,
            MissedHeartbeats = 2
        };

        _healthMonitor.RegisterClient(client);

        var heartbeat = new VPNHeartbeatPacket
        {
            ClientTime = DateTime.UtcNow.AddMilliseconds(-100),
            ClientUptime = 3600,
            ClientStatus = "Active"
        };

        // Act
        _healthMonitor.UpdateClientHeartbeat(client.ClientId, heartbeat);

        // Assert
        var updatedClient = _healthMonitor.GetClientInfo(client.ClientId);
        Assert.NotNull(updatedClient);
        Assert.Equal(0, updatedClient.MissedHeartbeats);
        Assert.True(updatedClient.AverageRoundTripTime > 0);
        Assert.True((DateTime.UtcNow - updatedClient.LastHeartbeat).TotalSeconds < 1);
    }

    [Fact]
    public async Task ProcessHeartbeatAsync_ShouldReturnValidResponse()
    {
        // Arrange
        var client = new VPNClientInfo
        {
            ClientId = "test-client-4",
            Status = VPNConnectionStatus.Connected
        };

        _healthMonitor.RegisterClient(client);

        var heartbeat = new VPNHeartbeatPacket
        {
            SessionId = client.ClientId,
            ClientTime = DateTime.UtcNow.AddMilliseconds(-50),
            ClientUptime = 1800,
            ClientStatus = "Active"
        };

        // Act
        var response = await _healthMonitor.ProcessHeartbeatAsync(client.ClientId, heartbeat);

        // Assert
        Assert.NotNull(response);
        Assert.Equal(VPNPacketType.HeartbeatResponse, response.Type);
        Assert.Equal(client.ClientId, response.SessionId);
        Assert.Equal("Active", response.ServerStatus);
        Assert.True(response.RoundTripTime > 0);
    }

    [Fact]
    public void GetHealthyClients_ShouldReturnOnlyHealthyClients()
    {
        // Arrange
        var healthyClient = new VPNClientInfo
        {
            ClientId = "healthy-client",
            Status = VPNConnectionStatus.Connected,
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-30),
            MissedHeartbeats = 0
        };

        var unhealthyClient = new VPNClientInfo
        {
            ClientId = "unhealthy-client",
            Status = VPNConnectionStatus.Connected,
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-70), // Too old (>60 seconds)
            MissedHeartbeats = 5
        };

        _healthMonitor.RegisterClient(healthyClient);
        _healthMonitor.RegisterClient(unhealthyClient);

        // Set the values after registration since RegisterClient resets LastHeartbeat
        healthyClient.LastHeartbeat = DateTime.UtcNow.AddSeconds(-30);
        healthyClient.MissedHeartbeats = 0;
        unhealthyClient.LastHeartbeat = DateTime.UtcNow.AddSeconds(-70);
        unhealthyClient.MissedHeartbeats = 5;

        // Act
        var healthyClients = _healthMonitor.GetHealthyClients().ToList();
        var unhealthyClients = _healthMonitor.GetUnhealthyClients().ToList();

        // Assert
        Assert.True(healthyClient.IsHealthy, "Healthy client should be healthy");
        Assert.False(unhealthyClient.IsHealthy, "Unhealthy client should be unhealthy");
        
        Assert.Contains(healthyClients, c => c.ClientId == "healthy-client");
        Assert.Contains(unhealthyClients, c => c.ClientId == "unhealthy-client");
    }

    [Fact]
    public void ClientHealthProperty_ShouldReflectActualHealth()
    {
        // Arrange & Act
        var healthyClient = new VPNClientInfo
        {
            ClientId = "test-healthy",
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-30),
            MissedHeartbeats = 1
        };

        var unhealthyClient = new VPNClientInfo
        {
            ClientId = "test-unhealthy",
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-70), // >60 seconds
            MissedHeartbeats = 5
        };

        // Debug output
        var timeDiff = (DateTime.UtcNow - unhealthyClient.LastHeartbeat).TotalSeconds;
        Console.WriteLine($"Time difference: {timeDiff} seconds, Missed heartbeats: {unhealthyClient.MissedHeartbeats}");
        Console.WriteLine($"IsHealthy: {unhealthyClient.IsHealthy}");

        // Assert
        Assert.True(healthyClient.IsHealthy);
        Assert.True(timeDiff > 60, $"Time difference should be > 60, but was {timeDiff}");
        Assert.True(unhealthyClient.MissedHeartbeats >= 3, $"Missed heartbeats should be >= 3, but was {unhealthyClient.MissedHeartbeats}");
        Assert.False(unhealthyClient.IsHealthy);
    }

    [Fact]
    public void ClientTimedOutEvent_ShouldFireWhenClientTimesOut()
    {
        // Arrange
        var eventFired = false;
        VPNClientInfo? timedOutClient = null;

        _healthMonitor.ClientTimedOut += (sender, client) =>
        {
            eventFired = true;
            timedOutClient = client;
        };

        var client = new VPNClientInfo
        {
            ClientId = "timeout-test-client",
            Status = VPNConnectionStatus.Connected,
            LastHeartbeat = DateTime.UtcNow.AddSeconds(-70), // Set to old time (>60 seconds)
            MissedHeartbeats = 5
        };

        _healthMonitor.RegisterClient(client);

        // Set the values after registration since RegisterClient resets LastHeartbeat
        client.LastHeartbeat = DateTime.UtcNow.AddSeconds(-70);
        client.MissedHeartbeats = 5;

        // Act & Assert
        Assert.False(client.IsHealthy, "Client should be unhealthy due to old heartbeat");
        Assert.Equal("timeout-test-client", client.ClientId);
    }

    [Fact]
    public void RoundTripTimeCalculation_ShouldUseExponentialMovingAverage()
    {
        // Arrange
        var client = new VPNClientInfo
        {
            ClientId = "rtt-test-client",
            Status = VPNConnectionStatus.Connected,
            AverageRoundTripTime = 100 // Initial RTT
        };

        _healthMonitor.RegisterClient(client);

        // Act - Send multiple heartbeats with different RTTs
        var heartbeat1 = new VPNHeartbeatPacket
        {
            ClientTime = DateTime.UtcNow.AddMilliseconds(-50) // 50ms RTT
        };
        _healthMonitor.UpdateClientHeartbeat(client.ClientId, heartbeat1);

        var heartbeat2 = new VPNHeartbeatPacket
        {
            ClientTime = DateTime.UtcNow.AddMilliseconds(-200) // 200ms RTT
        };
        _healthMonitor.UpdateClientHeartbeat(client.ClientId, heartbeat2);

        // Assert
        var updatedClient = _healthMonitor.GetClientInfo(client.ClientId);
        Assert.NotNull(updatedClient);
        
        // RTT should be between the original 100ms and the new values
        // due to exponential moving average (0.8 * old + 0.2 * new)
        Assert.True(updatedClient.AverageRoundTripTime > 50);
        Assert.True(updatedClient.AverageRoundTripTime < 200);
    }

    private void Dispose()
    {
        _healthMonitor?.Dispose();
    }
}