using System.Net.Sockets;
using System.Text;

namespace DataGateCertManager.Services.OpenVpnTelnet;

public class TelnetClient(string host, int port, ILogger<TelnetClient> logger) : IAsyncDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource _cancellationTokenSource = new();
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public event Action<string> OnDataReceived = delegate { };

    private bool IsConnected =>
        _client is { Connected: true } &&
        _stream is { CanRead: true, CanWrite: true };

    public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        await _connectionLock.WaitAsync(cancellationToken);
        try
        {
            if (IsConnected)
            {
                try
                {
                    await _writer!.WriteLineAsync(""); // ping
                    await _writer.FlushAsync(cancellationToken);
                    logger.LogDebug("✔ TelnetClient connection still alive.");
                    return;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "⚠ TelnetClient write check failed — reconnecting.");
                }
            }

            logger.LogInformation("🔌 TelnetClient reconnecting...");
            await DisconnectAsync();
            await ConnectCoreAsync(cancellationToken);
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task ConnectCoreAsync(CancellationToken cancellationToken, int timeoutSec = 5)
    {
        logger.LogInformation("🔌 Attempting to connect to OpenVPN management at {Host}:{Port}...", host, port);

        _client = new TcpClient();

        var connectTask = _client.ConnectAsync(host, port, cancellationToken).AsTask();
        if (await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(timeoutSec), cancellationToken)) != connectTask)
        {
            logger.LogError("⏰ Connection to OpenVPN management at {Host}:{Port} timed out after {Timeout}s.", host, port, timeoutSec);
            throw new TimeoutException($"Timeout while connecting to {host}:{port}");
        }

        logger.LogInformation("✅ TCP connection established to {Host}:{Port}.", host, port);

        _stream = _client.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII);
        _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };

        _cancellationTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => ListenAsync(_cancellationTokenSource.Token), cancellationToken);

        logger.LogInformation("📡 Telnet listener started.");
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        try
        {
            var buffer = new char[1024];
            var response = new StringBuilder();

            while (!cancellationToken.IsCancellationRequested)
            {
                int bytesRead = await _reader!.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0)
                {
                    await Task.Delay(100, cancellationToken);
                    continue;
                }

                response.Append(buffer, 0, bytesRead);
                string message = response.ToString();

                if (message.Contains("END") || message.Contains("SUCCESS:", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("ERROR:", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("NOTIFY:", StringComparison.OrdinalIgnoreCase)
                    || message.Contains("NOTICE:", StringComparison.OrdinalIgnoreCase))
                {
                    OnDataReceived.Invoke(message.Trim());
                    response.Clear();
                }
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
            logger.LogWarning("[TelnetClient] Connection closed: {Message}", ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "[TelnetClient] Unexpected error in listener.");
        }
    }

    public async Task SendAsync(string command, CancellationToken cancellationToken)
    {
        if (!IsConnected)
        {
            logger.LogWarning("[TelnetClient] Not connected, attempting reconnect before send...");
            await EnsureConnectedAsync(cancellationToken);
        }

        try
        {
            logger.LogInformation("➡ Sending command: {Command}", command);
            await _writer!.WriteLineAsync(command.AsMemory(), cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Failed to send command.");
            throw new IOException($"[TelnetClient] Failed to send command: {ex.Message}", ex);
        }
    }

    public async Task DisconnectAsync()
    {
        await _cancellationTokenSource.CancelAsync();

        try { if (_writer is not null) await _writer.DisposeAsync(); }
        catch (Exception ex) { logger.LogWarning("Writer dispose failed: {Message}", ex.Message); }

        try { _reader?.Dispose(); }
        catch (Exception ex) { logger.LogWarning("Reader dispose failed: {Message}", ex.Message); }

        try { if (_stream is not null) await _stream.DisposeAsync(); }
        catch (Exception ex) { logger.LogWarning("Stream dispose failed: {Message}", ex.Message); }

        try { _client?.Close(); }
        catch (Exception ex) { logger.LogWarning("Client close failed: {Message}", ex.Message); }

        _writer = null;
        _reader = null;
        _stream = null;
        _client = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync();
    }
}