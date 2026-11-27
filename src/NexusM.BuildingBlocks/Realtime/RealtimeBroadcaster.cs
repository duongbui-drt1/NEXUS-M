using System.Collections.Concurrent;
using System.Threading.Channels;

namespace NexusM.BuildingBlocks.Realtime;

public sealed record RealtimeEventMessage(
    string EventType,
    Guid TenantId,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc,
    object Payload);

public interface IRealtimeBroadcaster
{
    void Publish(RealtimeEventMessage message);
    ChannelReader<RealtimeEventMessage> Subscribe(Guid tenantId, out Guid subscriptionId);
    void Unsubscribe(Guid tenantId, Guid subscriptionId);
}

/// <summary>
/// Tenant-isolated SSE broadcaster ensuring realtime events never cross tenant boundaries (ADR-005).
/// </summary>
public sealed class InMemoryRealtimeBroadcaster : IRealtimeBroadcaster
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<Guid, Channel<RealtimeEventMessage>>> _subscribers = new();

    public void Publish(RealtimeEventMessage message)
    {
        if (!_subscribers.TryGetValue(message.TenantId, out var tenantSubs))
        {
            return;
        }

        foreach (var ch in tenantSubs.Values)
        {
            ch.Writer.TryWrite(message);
        }
    }

    public ChannelReader<RealtimeEventMessage> Subscribe(Guid tenantId, out Guid subscriptionId)
    {
        subscriptionId = Guid.CreateVersion7();
        var channel = Channel.CreateBounded<RealtimeEventMessage>(new BoundedChannelOptions(64)
        {
            FullMode = BoundedChannelFullMode.DropOldest
        });

        var tenantSubs = _subscribers.GetOrAdd(tenantId, _ => new ConcurrentDictionary<Guid, Channel<RealtimeEventMessage>>());
        tenantSubs[subscriptionId] = channel;
        return channel.Reader;
    }

    public void Unsubscribe(Guid tenantId, Guid subscriptionId)
    {
        if (_subscribers.TryGetValue(tenantId, out var tenantSubs) &&
            tenantSubs.TryRemove(subscriptionId, out var ch))
        {
            ch.Writer.TryComplete();
        }
    }
}
