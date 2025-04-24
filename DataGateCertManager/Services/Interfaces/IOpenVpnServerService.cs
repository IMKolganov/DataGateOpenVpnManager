namespace DataGateCertManager.Services.Interfaces;

public interface IOpenVpnServerService
{
    Task<string> BuildTlsAuthKeyAsync(string easyRsaPath, CancellationToken cancellationToken);
}