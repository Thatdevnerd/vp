using System.Net;

namespace VPNCore.Models;

public class VPNSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public IPEndPoint ClientEndPoint { get; set; } = new(IPAddress.Any, 0);
    public IPAddress AssignedVirtualIP { get; set; } = IPAddress.Any;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public VPNSessionState State { get; set; } = VPNSessionState.Connecting;
    public byte[] EncryptionKey { get; set; } = Array.Empty<byte>();
    public byte[] AuthenticationKey { get; set; } = Array.Empty<byte>();
    public uint LastSequenceNumber { get; set; }
    public long BytesSent { get; set; }
    public long BytesReceived { get; set; }
    public string ClientVersion { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public List<IPAddress> AllowedIPs { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public enum VPNSessionState
{
    Connecting,
    Authenticating,
    KeyExchange,
    Connected,
    Disconnecting,
    Disconnected,
    Error
}

public class VPNSessionManager
{
    private readonly Dictionary<string, VPNSession> _sessions = new();
    private readonly object _lock = new();

    public VPNSession CreateSession(IPEndPoint clientEndPoint)
    {
        lock (_lock)
        {
            var session = new VPNSession
            {
                ClientEndPoint = clientEndPoint,
                SessionId = Guid.NewGuid().ToString()
            };
            _sessions[session.SessionId] = session;
            return session;
        }
    }

    public VPNSession? GetSession(string sessionId)
    {
        lock (_lock)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }
    }

    public void RemoveSession(string sessionId)
    {
        lock (_lock)
        {
            _sessions.Remove(sessionId);
        }
    }

    public IEnumerable<VPNSession> GetAllSessions()
    {
        lock (_lock)
        {
            return _sessions.Values.ToList();
        }
    }

    public void UpdateLastActivity(string sessionId)
    {
        lock (_lock)
        {
            if (_sessions.TryGetValue(sessionId, out var session))
            {
                session.LastActivity = DateTime.UtcNow;
            }
        }
    }

    public void CleanupExpiredSessions(TimeSpan timeout)
    {
        lock (_lock)
        {
            var expiredSessions = _sessions.Values
                .Where(s => DateTime.UtcNow - s.LastActivity > timeout)
                .Select(s => s.SessionId)
                .ToList();

            foreach (var sessionId in expiredSessions)
            {
                _sessions.Remove(sessionId);
            }
        }
    }
}