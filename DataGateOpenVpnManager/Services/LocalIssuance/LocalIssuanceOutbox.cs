using DataGateMonitor.SharedModels.DataGateMonitor.LocalIssuance.Requests;
using Newtonsoft.Json;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>
/// Durable FIFO of <see cref="LocalIssuanceEventRequest"/> on the node's data volume: one fsync'ed JSON file per event,
/// deleted only after the backend acknowledged it, so container restarts and backend outages lose nothing.
/// </summary>
public interface ILocalIssuanceOutbox
{
    Task EnqueueAsync(LocalIssuanceEventRequest evt, CancellationToken cancellationToken);

    /// <summary>Pending events, oldest first.</summary>
    IReadOnlyList<LocalIssuanceOutboxItem> ListPending();

    void Complete(LocalIssuanceOutboxItem item);

    /// <summary>Backend refused the event as malformed; keep it aside for the operator instead of retrying forever.</summary>
    void Reject(LocalIssuanceOutboxItem item, string reason);

    /// <summary>Completes when something was enqueued since the last call (or on cancellation).</summary>
    Task WaitForEnqueueAsync(TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed record LocalIssuanceOutboxItem(string Path, LocalIssuanceEventRequest Event);

public sealed class LocalIssuanceOutbox(LocalIssuanceOptions options, ILogger<LocalIssuanceOutbox> logger)
    : ILocalIssuanceOutbox
{
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly object _sync = new();

    public Task EnqueueAsync(LocalIssuanceEventRequest evt, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(SafeSerial(evt.SerialNumber)))
            throw new ArgumentException("SerialNumber is required (idempotency key).", nameof(evt));
        lock (_sync)
        {
            PrivateFiles.EnsurePrivateDirectory(options.Directory);
            PrivateFiles.EnsurePrivateDirectory(options.OutboxDir);

            var suffix = $"-{evt.EventType}-{SafeSerial(evt.SerialNumber)}.json";
            if (Directory.EnumerateFiles(options.OutboxDir, "*" + suffix).Any())
            {
                logger.LogInformation("Event {EventType} for serial {Serial} is already queued.", evt.EventType, evt.SerialNumber);
                return Task.CompletedTask;
            }

            // Strictly increasing ticks keep FIFO order across restarts; the serial makes names unique per event.
            var ticks = Math.Max(DateTimeOffset.UtcNow.UtcTicks, LastQueuedTicks() + 1);
            var name = $"{ticks:D20}{suffix}";
            PrivateFiles.WriteAllTextAtomic(Path.Combine(options.OutboxDir, name), JsonConvert.SerializeObject(evt));
        }

        _signal.Release();
        return Task.CompletedTask;
    }

    public IReadOnlyList<LocalIssuanceOutboxItem> ListPending()
    {
        if (!Directory.Exists(options.OutboxDir))
            return [];

        var items = new List<LocalIssuanceOutboxItem>();
        foreach (var path in Directory.EnumerateFiles(options.OutboxDir, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                var evt = JsonConvert.DeserializeObject<LocalIssuanceEventRequest>(File.ReadAllText(path));
                if (evt is not null)
                    items.Add(new LocalIssuanceOutboxItem(path, evt));
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                logger.LogError(ex, "Unreadable outbox file {Path}; moving it aside.", path);
                MoveAside(path, "unreadable");
            }
        }

        return items;
    }

    public void Complete(LocalIssuanceOutboxItem item)
    {
        if (File.Exists(item.Path))
            File.Delete(item.Path);
    }

    public void Reject(LocalIssuanceOutboxItem item, string reason)
    {
        logger.LogError("Backend rejected {EventType} for {CommonName} (serial {Serial}): {Reason}. Kept in {Dir}.",
            item.Event.EventType, item.Event.CommonName, item.Event.SerialNumber, reason, options.RejectedDir);
        MoveAside(item.Path, reason);
    }

    public async Task WaitForEnqueueAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (await _signal.WaitAsync(timeout, cancellationToken))
        {
            // Collapse a burst of enqueues into one wake-up.
            while (_signal.CurrentCount > 0 && _signal.Wait(0)) { }
        }
    }

    private long LastQueuedTicks() =>
        Directory.EnumerateFiles(options.OutboxDir, "*.json")
            .Select(f => long.TryParse(Path.GetFileName(f).Split('-')[0], out var t) ? t : 0)
            .DefaultIfEmpty(0)
            .Max();

    private void MoveAside(string path, string reason)
    {
        PrivateFiles.EnsurePrivateDirectory(options.RejectedDir);
        var target = Path.Combine(options.RejectedDir, Path.GetFileName(path));
        File.Move(path, target, overwrite: true);
        PrivateFiles.WriteAllTextAtomic(target + ".reason.txt", reason);
    }

    private static string SafeSerial(string serial) =>
        new(serial.Where(char.IsAsciiLetterOrDigit).ToArray());
}
