namespace DataGateOpenVpnManager.Services.Interfaces;

public interface IOpenVpnServerService
{
    Task<string> BuildTlsAuthKeyAsync(string easyRsaPath, string taKeyName,
        CancellationToken cancellationToken);
}