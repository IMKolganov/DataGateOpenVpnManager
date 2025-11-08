using System.Collections.Concurrent;

namespace DataGateOpenVpnManager.Services.OpenVpnTelnet;

public class CommandQueue : ICommandQueue, IAsyncDisposable
{
    private readonly ConcurrentQueue<string> _messageQueue = new();
    private readonly ConcurrentQueue<PendingCommand> _pendingCommands = new();
    private readonly TelnetClient _telnetClient;
    private readonly List<IMessageSubscriber> _subscribers = new();
    private readonly Lock _subscriberLock = new();

    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<CommandQueue> _logger;

    private record PendingCommand(string CommandText, TaskCompletionSource<string> TaskSource);

    public bool HasSubscribers
    {
        get
        {
            lock (_subscriberLock)
                return _subscribers.Count > 0;
        }
    }

    public CommandQueue(TelnetClient telnetClient, ILogger<CommandQueue> logger)
    {
        _telnetClient = telnetClient;
        _logger = logger;

        _telnetClient.OnDataReceived += message => HandleIncomingMessage(message, _cts.Token);
    }

    public void Subscribe(IMessageSubscriber subscriber)
    {
        lock (_subscriberLock)
            _subscribers.Add(subscriber);
    }

    public void Unsubscribe(IMessageSubscriber subscriber, string ip, int port)
    {
        bool removed;
        lock (_subscriberLock)
        {
            removed = _subscribers.Remove(subscriber);
        }

        if (!removed)
            throw new Exception("Subscriber doesn't exist");
    }

    private void HandleIncomingMessage(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var trimmed = message.Trim();

        if (trimmed.Contains("END", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("SUCCESS:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("NOTIFY:", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Contains("NOTICE:", StringComparison.OrdinalIgnoreCase))
        {
            if (_pendingCommands.TryDequeue(out var pending))
            {
                if (!pending.TaskSource.Task.IsCompleted)
                    pending.TaskSource.TrySetResult(trimmed);
            }
            else
            {
                _logger.LogWarning("[CommandQueue] No pending command found, adding to queue.");
                _messageQueue.Enqueue(trimmed);
                NotifySubscribers(trimmed, cancellationToken);
            }
        }
        else
        {
            _logger.LogDebug("[CommandQueue] Message not complete, adding to unprocessed queue.");
            _messageQueue.Enqueue(trimmed);
            NotifySubscribers(trimmed, cancellationToken);
        }
    }

    private void NotifySubscribers(string message, CancellationToken cancellationToken)
    {
        List<IMessageSubscriber> snapshot;
        lock (_subscriberLock)
            snapshot = _subscribers.ToList();

        foreach (var subscriber in snapshot)
        {
            try
            {
                subscriber.OnMessageReceived(message, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CommandQueue] Failed to notify subscriber.");
            }
        }
    }

    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken, int timeoutMs = 15000)
    {
        if (_cts.IsCancellationRequested)
            throw new InvalidOperationException("[CommandQueue] Cannot send command — queue is disconnected.");
        
        cancellationToken.ThrowIfCancellationRequested();

        await _telnetClient.EnsureConnectedAsync(cancellationToken);

        var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pendingCommand = new PendingCommand(command, tcs);

        _pendingCommands.Enqueue(pendingCommand);

        try
        {
            await _telnetClient.SendAsync(command, cancellationToken);

            var timeoutTask = Task.Delay(timeoutMs, cancellationToken);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == tcs.Task)
            {
                var result = await tcs.Task; // can rethrow if tcs.Task faulted
                _logger.LogInformation("[CommandQueue] ✅ Received response for command: {Command} → {Result}", command, result);
                return result;
            }
            
            if (_pendingCommands.TryDequeue(out var timedOutCommand) && timedOutCommand == pendingCommand)
            {
                tcs.TrySetCanceled();
            }

            throw new TimeoutException($"[CommandQueue] Command \"{command}\" timed out after {timeoutMs}ms.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CommandQueue] Exception in SendCommandAsync");

            if (_pendingCommands.TryDequeue(out var erroredCommand) && erroredCommand == pendingCommand)
            {
                tcs.TrySetException(ex);
            }

            throw;
        }
    }


    public (bool result, string? message) TryGetMessage()
    {
        var result = _messageQueue.TryDequeue(out var message);
        return (result, message);
    }

    public async Task<bool> IsAliveAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendCommandAsync("echo", cancellationToken, timeoutMs: 2000);
            return !string.IsNullOrWhiteSpace(response);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CommandQueue] IsAliveAsync failed");
            return false;
        }
    }

    public async Task DisconnectAsync()
    {
        await _cts.CancelAsync();

        if (!HasSubscribers)
        {
            await _telnetClient.DisconnectAsync();
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[CommandQueue] DisposeAsync failed");
        }
    }
}
