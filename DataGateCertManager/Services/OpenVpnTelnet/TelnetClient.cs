using System.Net.Sockets;
using System.Text;

namespace DataGateCertManager.Services.OpenVpnTelnet;

public class TelnetClient(string host, int port, ILogger<TelnetClient> logger) : IDisposable
{
    private TcpClient? _client;
    private NetworkStream? _stream;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private CancellationTokenSource _cancellationTokenSource = new();
    public event Action<string> OnDataReceived = delegate { };

    public async Task ConnectAsync(CancellationToken cancellationToken, int timeoutSec = 5)
    {
        _client = new TcpClient();

        var connectTask = _client.ConnectAsync(host, port, cancellationToken).AsTask();

        if (await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromSeconds(timeoutSec), cancellationToken)) != connectTask)
        {
            throw new TimeoutException($"Timeout while connecting to {host}:{port}");
        }

        _stream = _client.GetStream();
        _reader = new StreamReader(_stream, Encoding.ASCII);
        _writer = new StreamWriter(_stream, Encoding.ASCII) { AutoFlush = true };

        _cancellationTokenSource = new CancellationTokenSource();
        _ = Task.Run(() => ListenAsync(_cancellationTokenSource.Token), cancellationToken);
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
                if (bytesRead == 0) continue;

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
            Console.WriteLine($"[TelnetClient] Connection closed: {ex.Message}");
        }
    }

    public async Task SendAsync(string command, CancellationToken cancellationToken)
    {
        if (_client == null || _stream == null || !_client.Connected || !_stream.CanWrite)
        {
            throw new IOException("[TelnetClient] Cannot send: Not connected or stream unavailable.");
        }

        try
        {
            await _writer!.WriteLineAsync(command.AsMemory(), cancellationToken);
        }
        catch (Exception ex)
        {
            throw new IOException($"[TelnetClient] Failed to send command: {ex.Message}", ex);
        }
    }

    public Task DisconnectAsync()
    {
        _cancellationTokenSource.Cancel();

        try { _writer?.Dispose(); } catch(Exception ex) { logger.LogWarning($"Ignore error: {ex.Message}"); }
        try { _reader?.Dispose(); } catch(Exception ex) { logger.LogWarning($"Ignore error: {ex.Message}"); }
        try { _stream?.Dispose(); } catch(Exception ex) { logger.LogWarning($"Ignore error: {ex.Message}"); }
        try { _client?.Close(); } catch(Exception ex) { logger.LogWarning($"Ignore error: {ex.Message}"); }

        _writer = null;
        _reader = null;
        _stream = null;
        _client = null;

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Task.Run(DisconnectAsync).Wait();
    }
}
