using DataGateCertManager.Services.EasyRsaServices.Interfaces;

namespace DataGateCertManager.Services.EasyRsaServices;


public class EasyRsaExecCommandService : IEasyRsaExecCommandService
{
    private readonly ILogger<IEasyRsaExecCommandService> _logger;
    private readonly IBashCommandRunner _bashCommandRunner;

    public EasyRsaExecCommandService(
        ILogger<IEasyRsaExecCommandService> logger,
        IBashCommandRunner bashCommandRunner)
    {
        _logger = logger;
        _bashCommandRunner = bashCommandRunner;
    }
    
    #region EasyRSA revoke command variations
// # =========================================================================================================================
// # EasyRSA revoke command variations                                                                                       |
// # =========================================================================================================================
// # | Command Example                                   | Description                                                       |
// # |---------------------------------------------------|-------------------------------------------------------------------|
// # | ./easyrsa revoke client1                          | Revokes the client certificate (client1)                          |
// # | EASYRSA_BATCH=1 ./easyrsa revoke client1          | Revokes the certificate without confirmation prompt               |
// # | EASYRSA_CRL_DAYS=3650 ./easyrsa revoke client1    | Sets the Certificate Revocation List (CRL) expiration to 10 years |
// # | ./easyrsa gen-crl                                 | Generates or updates the Certificate Revocation List (CRL)        |
// # | EASYRSA_CRL_DAYS=7300 ./easyrsa gen-crl           | Generates a CRL valid for 20 years                                |
// # =========================================================================================================================
    #endregion
    public async Task<(bool IsSuccess, string Output, int ExitCode, string Error)> ExecuteEasyRsaCommand(
        string arguments,
        string easyRsaPath,
        CancellationToken cancellationToken,
        bool confirm = false)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            var commandPrefix = $"cd {easyRsaPath} &&";
            var fullArgs = confirm ? $"EASYRSA_BATCH=1 ./easyrsa {arguments}" : $"./easyrsa {arguments}";
            var command = $"{commandPrefix} {fullArgs}";

            _logger.LogInformation($"Executing command: {command}");
            var result = await RunCommand(command, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (result.ExitCode == 0)
            {
                _logger.LogInformation($"Command executed successfully: {result.Output}");
                return (true, result.Output, result.ExitCode, string.Empty);
            }

            _logger.LogWarning($"Command failed with exit code {result.ExitCode}: {result.Error}");
            return (false, result.Output, result.ExitCode, result.Error);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Easy-RSA command execution was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Exception during Easy-RSA command execution: {ex.Message}");
            return (false, string.Empty, 500, ex.Message);
        }
    }

    public async Task<(string Output, string Error, int ExitCode)> RunCommand(string command,
        CancellationToken cancellationToken) 
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await _bashCommandRunner.RunCommandAsync(command, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation($"Command execution was cancelled: {command}");
            throw;
        }
    }
}