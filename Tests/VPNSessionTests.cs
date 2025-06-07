using System.Net;
using VPNCore.Models;
using Xunit;

namespace VPNTests;

public class VPNSessionTests
{
    [Fact]
    public void CreateSession_ShouldGenerateUniqueSessionId()
    {
        // Arrange
        var sessionManager = new VPNSessionManager();
        var endPoint = new IPEndPoint(IPAddress.Loopback, 12345);

        // Act
        var session1 = sessionManager.CreateSession(endPoint);
        var session2 = sessionManager.CreateSession(endPoint);

        // Assert
        Assert.NotEqual(session1.SessionId, session2.SessionId);
        Assert.Equal(endPoint, session1.ClientEndPoint);
        Assert.Equal(endPoint, session2.ClientEndPoint);
    }

    [Fact]
    public void GetSession_ShouldReturnCorrectSession()
    {
        // Arrange
        var sessionManager = new VPNSessionManager();
        var endPoint = new IPEndPoint(IPAddress.Loopback, 12345);
        var session = sessionManager.CreateSession(endPoint);

        // Act
        var retrievedSession = sessionManager.GetSession(session.SessionId);

        // Assert
        Assert.NotNull(retrievedSession);
        Assert.Equal(session.SessionId, retrievedSession.SessionId);
        Assert.Equal(session.ClientEndPoint, retrievedSession.ClientEndPoint);
    }

    [Fact]
    public void GetSession_WithInvalidId_ShouldReturnNull()
    {
        // Arrange
        var sessionManager = new VPNSessionManager();

        // Act
        var session = sessionManager.GetSession("invalid-session-id");

        // Assert
        Assert.Null(session);
    }

    [Fact]
    public void RemoveSession_ShouldRemoveSession()
    {
        // Arrange
        var sessionManager = new VPNSessionManager();
        var endPoint = new IPEndPoint(IPAddress.Loopback, 12345);
        var session = sessionManager.CreateSession(endPoint);

        // Act
        sessionManager.RemoveSession(session.SessionId);
        var retrievedSession = sessionManager.GetSession(session.SessionId);

        // Assert
        Assert.Null(retrievedSession);
    }

    [Fact]
    public void UpdateLastActivity_ShouldUpdateTimestamp()
    {
        // Arrange
        var sessionManager = new VPNSessionManager();
        var endPoint = new IPEndPoint(IPAddress.Loopback, 12345);
        var session = sessionManager.CreateSession(endPoint);
        var originalTime = session.LastActivity;

        // Act
        Thread.Sleep(10); // Ensure time difference
        sessionManager.UpdateLastActivity(session.SessionId);
        var updatedSession = sessionManager.GetSession(session.SessionId);

        // Assert
        Assert.NotNull(updatedSession);
        Assert.True(updatedSession.LastActivity > originalTime);
    }

    [Fact]
    public void CleanupExpiredSessions_ShouldRemoveOldSessions()
    {
        // Arrange
        var sessionManager = new VPNSessionManager();
        var endPoint = new IPEndPoint(IPAddress.Loopback, 12345);
        var session = sessionManager.CreateSession(endPoint);
        
        // Manually set old timestamp
        session.LastActivity = DateTime.UtcNow.AddMinutes(-10);

        // Act
        sessionManager.CleanupExpiredSessions(TimeSpan.FromMinutes(5));
        var retrievedSession = sessionManager.GetSession(session.SessionId);

        // Assert
        Assert.Null(retrievedSession);
    }

    [Fact]
    public void GetAllSessions_ShouldReturnAllActiveSessions()
    {
        // Arrange
        var sessionManager = new VPNSessionManager();
        var endPoint1 = new IPEndPoint(IPAddress.Loopback, 12345);
        var endPoint2 = new IPEndPoint(IPAddress.Loopback, 12346);
        
        var session1 = sessionManager.CreateSession(endPoint1);
        var session2 = sessionManager.CreateSession(endPoint2);

        // Act
        var allSessions = sessionManager.GetAllSessions().ToList();

        // Assert
        Assert.Equal(2, allSessions.Count);
        Assert.Contains(allSessions, s => s.SessionId == session1.SessionId);
        Assert.Contains(allSessions, s => s.SessionId == session2.SessionId);
    }
}