using System.Collections.Concurrent;

namespace DataGateOpenVpnManager.Hubs;


public sealed class HubConnectionTracker
{
    private readonly ConcurrentDictionary<string, byte> _eventHub = new();
    private readonly ConcurrentDictionary<string, byte> _signalHub = new();

    public int EventHubCount => _eventHub.Count;
    public int SignalHubCount => _signalHub.Count;

    public DateTimeOffset LastHeartbeatUtc { get; private set; } = DateTimeOffset.MinValue;

    public void EventHubConnected(string id) => _eventHub[id] = 0;
    public void EventHubDisconnected(string id) => _eventHub.TryRemove(id, out _);

    public void SignalHubConnected(string id) => _signalHub[id] = 0;
    public void SignalHubDisconnected(string id) => _signalHub.TryRemove(id, out _);

    public void TouchHeartbeat() => LastHeartbeatUtc = DateTimeOffset.UtcNow;
}