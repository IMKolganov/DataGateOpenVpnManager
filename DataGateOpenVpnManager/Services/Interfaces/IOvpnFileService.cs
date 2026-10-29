
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.OvpnFile.Responses;

namespace DataGateOpenVpnManager.Services.Interfaces;

public interface IOvpnFileService
{
    /// <summary>
    /// Single issuance pipeline (easy-rsa + template + optional service profile) shared by the
    /// dashboard API and the node-local admin channel.
    /// </summary>
    Task<OvpnIssueResult> IssueOvpnFile(OvpnIssueRequest request, CancellationToken cancellationToken);

    Task<OvpnFileMetadata> AddOvpnFile(string easyRsaPath, string commonName, string friendlyΝame,string configTemplate, 
        string serverIp, int serverPort, CancellationToken cancellationToken, 
        string issuedTo = "openVpnClient", int certExpireDays = 365);

    Task<OvpnFileMetadata?> RevokeOvpnFile(string easyRsaPath, string commonName,
        string ovpnFileName, string ovpnFilePath, CancellationToken cancellationToken);
    Task<OvpnFileDownload> GetOvpnFile(
        string fileName,
        string filePath,
        string? commonName,
        CancellationToken cancellationToken);
}
public sealed class OvpnIssueRequest
{
    public required string EasyRsaPath { get; init; }
    public required string CommonName { get; init; }
    public string FriendlyName { get; init; } = string.Empty;
    public required string ConfigTemplate { get; init; }
    public required string ServerIp { get; init; }
    public int ServerPort { get; init; }
    public string IssuedTo { get; init; } = "openVpnClient";
    public int CertExpireDays { get; init; } = 365;

    /// <summary>Only VPN-internal traffic goes through the tunnel (pull-filter for gateway/DNS pushes).</summary>
    public bool IsServiceProfile { get; init; }
}

public sealed record OvpnIssueResult(
    OvpnFileMetadata Metadata,
    ServerCertificate Certificate,
    string CertFingerprintSha256);
