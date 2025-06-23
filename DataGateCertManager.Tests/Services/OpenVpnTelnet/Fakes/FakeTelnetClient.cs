using DataGateCertManager.Services.OpenVpnTelnet;
using Microsoft.Extensions.Logging;

namespace DataGateCertManager.Tests.Services.OpenVpnTelnet.Fakes;

public class FakeTelnetClient() : TelnetClient("localhost", 1234, new LoggerFactory().CreateLogger<TelnetClient>())
{
    public int DisconnectCallCount { get; private set; }
    public bool ThrowOnSend { get; set; }
    public Exception? ExceptionToThrow { get; set; }
    public List<string> SentCommands { get; } = new();

    public override Task SendAsync(string command, CancellationToken cancellationToken)
    {
        if (ThrowOnSend && ExceptionToThrow != null)
            throw ExceptionToThrow;

        SentCommands.Add(command);
        return Task.CompletedTask;
    }

    public override Task EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public override Task DisconnectAsync()
    {
        DisconnectCallCount++;
        return Task.CompletedTask;
    }

    public void SimulateIncomingData(string message)
    {
        RaiseOnDataReceived(message);
    }

    public void Reset()
    {
        DisconnectCallCount = 0;
        ThrowOnSend = false;
        ExceptionToThrow = null;
        SentCommands.Clear();
    }
}