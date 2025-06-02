namespace DataGateCertManager.Services.OpenVpnTelnet;

public class OpenVpnManagementSignalService(TelnetClient telnetClient, ILogger<CommandQueue> commandQueueLogger)
{
    private readonly CommandQueue _commandQueue = new(telnetClient, commandQueueLogger);
    
    public async Task<string> SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            commandQueueLogger.LogInformation($"Sending command... command: {command}");
            return await _commandQueue.SendCommandAsync(command, cancellationToken);
        }
        catch (TimeoutException ex)
        {
            commandQueueLogger.LogError(ex.Message);
            return $"Command timed out: {ex.Message}";
        }
        catch (Exception ex)
        {
            commandQueueLogger.LogError(ex.Message);
            return $"Error while sending command: {ex.Message}";
        }
    }

    public void Subscribe(IMessageSubscriber subscriber) => _commandQueue.Subscribe(subscriber);
    public void Unsubscribe(IMessageSubscriber subscriber, string ip, int port) => _commandQueue.Unsubscribe(subscriber, ip, port);
}