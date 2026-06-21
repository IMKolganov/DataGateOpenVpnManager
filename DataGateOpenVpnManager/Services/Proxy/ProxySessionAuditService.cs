using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Proxy;
using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Services.Proxy;

public interface IProxySessionAuditService
{
    void Record(ProxySessionAuditEntry entry);
    IReadOnlyList<ProxySessionAuditEntry> GetRecent(int limit);
}

public sealed record ProxySessionAuditEntry
{
    public required DateTime AtUtc { get; init; }
    public required string Event { get; init; }
    public string? ConnectionId { get; init; }
    public string? Decision { get; init; }
    public string? Reason { get; init; }
    public IReadOnlyDictionary<string, string>? Details { get; init; }
}

public sealed class ProxySessionAuditService(
    IOptions<OpenVpnProxyOptions> options,
    ILogger<ProxySessionAuditService> logger) : IProxySessionAuditService
{
    private readonly object _sync = new();
    private readonly Queue<ProxySessionAuditEntry> _entries = new();

    public void Record(ProxySessionAuditEntry entry)
    {
        if (!options.Value.IsSessionAuditEnabled)
            return;

        var bufferSize = Math.Clamp(options.Value.SessionAuditBufferSize, 50, 5000);
        lock (_sync)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > bufferSize)
                _entries.Dequeue();
        }

        logger.LogInformation(
            "[ProxyAudit] event={Event} decision={Decision} connectionId={ConnectionId} reason={Reason} {Details}",
            entry.Event,
            entry.Decision ?? "-",
            entry.ConnectionId ?? "-",
            entry.Reason ?? "-",
            FormatDetails(entry.Details));
    }

    public IReadOnlyList<ProxySessionAuditEntry> GetRecent(int limit)
    {
        var take = Math.Clamp(limit, 1, 2000);
        lock (_sync)
        {
            if (_entries.Count <= take)
                return _entries.ToArray();

            return _entries.Skip(_entries.Count - take).ToArray();
        }
    }

    private static string FormatDetails(IReadOnlyDictionary<string, string>? details)
    {
        if (details is null || details.Count == 0)
            return string.Empty;

        return string.Join(' ', details.Select(kv => $"{kv.Key}={kv.Value}"));
    }
}
