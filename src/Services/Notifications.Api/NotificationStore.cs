using System.Collections.Concurrent;

namespace Notifications.Api;

public record Notification(Guid Id, Guid OrderId, string To, string Subject, string Body, DateTime SentAtUtc);

/// <summary>
/// In-memory record of the last 100 "emails" sent, so you can see them via the API.
/// Not every service needs a database: Notifications only reacts to events. The trade-off
/// is that this list (and the duplicate check) is lost when the container restarts.
/// </summary>
public class NotificationStore
{
    private const int Capacity = 100;

    private readonly ConcurrentDictionary<Guid, byte> _handledMessageIds = new();
    private readonly LinkedList<Notification> _sent = new();
    private readonly Lock _lock = new();

    public bool IsHandled(Guid messageId) => _handledMessageIds.ContainsKey(messageId);

    public void Record(Guid messageId, Notification notification)
    {
        lock (_lock)
        {
            _sent.AddFirst(notification);
            if (_sent.Count > Capacity)
            {
                _sent.RemoveLast();
            }
        }

        _handledMessageIds.TryAdd(messageId, 0);
    }

    public IReadOnlyList<Notification> GetRecent()
    {
        lock (_lock)
        {
            return _sent.ToList();
        }
    }
}
