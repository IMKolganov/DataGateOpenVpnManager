using System.Collections.Concurrent;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy.Enums;

namespace DataGateOpenVpnManager.Services.Proxy;

public sealed class ProxyTrafficFlowService : IProxyTrafficFlowService
{
    private static readonly TimeSpan IdleThreshold = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<string, FlowConnectionState> _connections = new();
    private readonly ConcurrentQueue<ProxyTrafficFlowUpdate> _terminalUpdates = new();

    public void RegisterConnection(ActiveProxyConnection connection, ProxyConnectionIdentity? identity = null)
    {
        var connectedAt = connection.ConnectedAtUtc == default ? DateTime.UtcNow : connection.ConnectedAtUtc;
        var state = new FlowConnectionState(
            connection.ConnectionId,
            connection.Protocol,
            connection.RealClientIp,
            connection.RealClientPort,
            identity?.ClientRef,
            identity?.UserId,
            identity?.Username,
            identity?.Email,
            connection.LocalProxyIp,
            connection.LocalProxyPort,
            connection.TargetIp,
            connection.TargetPort,
            connectedAt);

        _connections[connection.ConnectionId] = state;
    }

    public void UnregisterConnection(string connectionId, DateTime? disconnectedAtUtc = null)
    {
        if (!_connections.TryRemove(connectionId, out var state))
            return;

        var emittedAt = disconnectedAtUtc ?? DateTime.UtcNow;
        lock (state.SyncRoot)
        {
            _terminalUpdates.Enqueue(new ProxyTrafficFlowUpdate
            {
                ConnectionId = state.ConnectionId,
                Protocol = state.Protocol,
                State = "disconnected",
                IsConnected = false,
                IsIdle = true,
                RealClientIp = state.RealClientIp,
                RealClientPort = state.RealClientPort,
                ClientRef = state.ClientRef,
                UserId = state.UserId,
                Username = state.Username,
                Email = state.Email,
                LocalProxyIp = state.LocalProxyIp,
                LocalProxyPort = state.LocalProxyPort,
                TargetIp = state.TargetIp,
                TargetPort = state.TargetPort,
                ClientToServerBytesTotal = state.ClientToServerBytesTotal,
                ServerToClientBytesTotal = state.ServerToClientBytesTotal,
                ClientToServerBytesDelta = state.ClientToServerBytesDelta,
                ServerToClientBytesDelta = state.ServerToClientBytesDelta,
                ConnectedAtUtc = state.ConnectedAtUtc,
                LastActivityAtUtc = state.LastActivityAtUtc,
                EmittedAtUtc = emittedAt
            });

            state.ClientToServerBytesDelta = 0;
            state.ServerToClientBytesDelta = 0;
        }
    }

    public void RegisterConnectFailed(
        string connectionId,
        ProxyConnectionProtocol protocol,
        string? realClientIp,
        int realClientPort,
        ProxyConnectionIdentity? identity,
        string targetIp,
        int targetPort,
        string? errorMessage,
        DateTime? failedAtUtc = null)
    {
        var at = failedAtUtc ?? DateTime.UtcNow;
        _terminalUpdates.Enqueue(new ProxyTrafficFlowUpdate
        {
            ConnectionId = connectionId,
            Protocol = protocol,
            State = "failed",
            IsConnected = false,
            IsIdle = true,
            RealClientIp = realClientIp,
            RealClientPort = realClientPort,
            ClientRef = identity?.ClientRef,
            UserId = identity?.UserId,
            Username = identity?.Username,
            Email = identity?.Email,
            LocalProxyIp = null,
            LocalProxyPort = 0,
            TargetIp = targetIp,
            TargetPort = targetPort,
            ClientToServerBytesTotal = 0,
            ServerToClientBytesTotal = 0,
            ClientToServerBytesDelta = 0,
            ServerToClientBytesDelta = 0,
            ConnectedAtUtc = at,
            LastActivityAtUtc = at,
            EmittedAtUtc = at,
            ErrorMessage = errorMessage
        });
    }

    public void RecordTraffic(
        string connectionId,
        ProxyTrafficFlowDirection direction,
        int bytes,
        DateTime? occurredAtUtc = null)
    {
        if (bytes <= 0)
            return;
        if (!_connections.TryGetValue(connectionId, out var state))
            return;

        var at = occurredAtUtc ?? DateTime.UtcNow;
        lock (state.SyncRoot)
        {
            if (direction == ProxyTrafficFlowDirection.ClientToServer)
            {
                state.ClientToServerBytesTotal += bytes;
                state.ClientToServerBytesDelta += bytes;
            }
            else
            {
                state.ServerToClientBytesTotal += bytes;
                state.ServerToClientBytesDelta += bytes;
            }

            state.LastActivityAtUtc = at;
        }
    }

    public IReadOnlyCollection<ProxyTrafficFlowUpdate> BuildBatch(DateTime emittedAtUtc)
    {
        var result = new List<ProxyTrafficFlowUpdate>(_connections.Count + _terminalUpdates.Count);
        foreach (var state in _connections.Values)
        {
            lock (state.SyncRoot)
            {
                var isIdle = emittedAtUtc - state.LastActivityAtUtc >= IdleThreshold;
                result.Add(new ProxyTrafficFlowUpdate
                {
                    ConnectionId = state.ConnectionId,
                    Protocol = state.Protocol,
                    State = "connected",
                    IsConnected = true,
                    IsIdle = isIdle,
                    RealClientIp = state.RealClientIp,
                    RealClientPort = state.RealClientPort,
                    ClientRef = state.ClientRef,
                    UserId = state.UserId,
                    Username = state.Username,
                    Email = state.Email,
                    LocalProxyIp = state.LocalProxyIp,
                    LocalProxyPort = state.LocalProxyPort,
                    TargetIp = state.TargetIp,
                    TargetPort = state.TargetPort,
                    ClientToServerBytesTotal = state.ClientToServerBytesTotal,
                    ServerToClientBytesTotal = state.ServerToClientBytesTotal,
                    ClientToServerBytesDelta = state.ClientToServerBytesDelta,
                    ServerToClientBytesDelta = state.ServerToClientBytesDelta,
                    ConnectedAtUtc = state.ConnectedAtUtc,
                    LastActivityAtUtc = state.LastActivityAtUtc,
                    EmittedAtUtc = emittedAtUtc
                });

                state.ClientToServerBytesDelta = 0;
                state.ServerToClientBytesDelta = 0;
            }
        }

        while (_terminalUpdates.TryDequeue(out var terminal))
        {
            result.Add(terminal);
        }

        return result;
    }

    private sealed class FlowConnectionState
    {
        public FlowConnectionState(
            string connectionId,
            ProxyConnectionProtocol protocol,
            string? realClientIp,
            int realClientPort,
            string? clientRef,
            string? userId,
            string? username,
            string? email,
            string? localProxyIp,
            int localProxyPort,
            string? targetIp,
            int targetPort,
            DateTime connectedAtUtc)
        {
            ConnectionId = connectionId;
            Protocol = protocol;
            RealClientIp = realClientIp;
            RealClientPort = realClientPort;
            ClientRef = clientRef;
            UserId = userId;
            Username = username;
            Email = email;
            LocalProxyIp = localProxyIp;
            LocalProxyPort = localProxyPort;
            TargetIp = targetIp;
            TargetPort = targetPort;
            ConnectedAtUtc = connectedAtUtc;
            LastActivityAtUtc = connectedAtUtc;
        }

        public object SyncRoot { get; } = new();
        public string ConnectionId { get; }
        public ProxyConnectionProtocol Protocol { get; }
        public string? RealClientIp { get; }
        public int RealClientPort { get; }
        public string? ClientRef { get; }
        public string? UserId { get; }
        public string? Username { get; }
        public string? Email { get; }
        public string? LocalProxyIp { get; }
        public int LocalProxyPort { get; }
        public string? TargetIp { get; }
        public int TargetPort { get; }
        public DateTime ConnectedAtUtc { get; }
        public DateTime LastActivityAtUtc { get; set; }
        public long ClientToServerBytesTotal { get; set; }
        public long ServerToClientBytesTotal { get; set; }
        public long ClientToServerBytesDelta { get; set; }
        public long ServerToClientBytesDelta { get; set; }
    }
}
