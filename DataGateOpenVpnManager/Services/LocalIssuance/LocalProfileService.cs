using System.Text.RegularExpressions;
using DataGateMonitor.SharedModels.DataGateOpenVpnManager.Cert.Responses;
using DataGateMonitor.SharedModels.Enums;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using DataGateOpenVpnManager.Services.Interfaces;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

public sealed class LocalProfileCreateRequest
{
    public string CommonName { get; set; } = string.Empty;
    public bool IsServiceProfile { get; set; }
    public string? StaticIp { get; set; }

    /// <summary>Host clients dial; defaults to PUBLIC_IP.</summary>
    public string? Remote { get; set; }

    /// <summary>Defaults to the node's OpenVPN PORT.</summary>
    public int? Port { get; set; }

    public int? ExpireDays { get; set; }
}

public sealed class LocalProfileView
{
    public string CommonName { get; set; } = string.Empty;
    public string Origin { get; set; } = "local";
    public string Status { get; set; } = "unknown";
    public string SerialNumber { get; set; } = string.Empty;
    public string? CertFingerprintSha256 { get; set; }
    public bool IsServiceProfile { get; set; }
    public string? StaticIp { get; set; }
    public DateTimeOffset? IssuedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

public interface ILocalProfileService
{
    Task<LocalProfileView> CreateAsync(LocalProfileCreateRequest request, CancellationToken cancellationToken);
    Task<LocalProfileView> RevokeAsync(string commonName, CancellationToken cancellationToken);
    Task<IReadOnlyList<LocalProfileView>> ListAsync(CancellationToken cancellationToken);
    Task<string> GetOvpnAsync(string commonName, CancellationToken cancellationToken);
}

/// <summary>
/// Node-local issuance: same <see cref="IOvpnFileService"/> pipeline as the dashboard, plus a local registry
/// and a durable event to the backend so the dashboard learns about the profile.
/// </summary>
public sealed partial class LocalProfileService(
    LocalIssuanceOptions options,
    IOvpnFileService ovpnFileService,
    IEasyRsaService easyRsaService,
    IEasyRsaPathResolver easyRsaPathResolver,
    ILocalProfileRegistry registry,
    ILocalIssuanceOutbox outbox,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<LocalProfileService> logger) : ILocalProfileService
{
    public const string IssuedToLocalAdmin = "local-admin";
    public const string IssuedToLocalService = "local-service";

    /// <summary>Used when <c>client-template.ovpn</c> is absent; proto/cipher/auth are aligned with the node afterwards.</summary>
    internal const string DefaultClientTemplate = """
        setenv FRIENDLY_NAME "{{friendly_name}}"
        client
        dev tun
        proto udp
        remote {{server_ip}} {{server_port}}
        resolv-retry infinite
        nobind
        remote-cert-tls server
        tls-version-min 1.2
        cipher AES-256-CBC
        auth SHA256
        auth-nocache
        verb 3
        <ca>
        {{ca_cert}}
        </ca>
        <cert>
        {{client_cert}}
        </cert>
        <key>
        {{client_key}}
        </key>
        <tls-crypt>
        {{tls_auth_key}}
        </tls-crypt>
        """;

    [GeneratedRegex("^[A-Za-z0-9](?:[A-Za-z0-9.:-]{0,252}[A-Za-z0-9])?$")]
    private static partial Regex RemoteHostPattern();

    public async Task<LocalProfileView> CreateAsync(LocalProfileCreateRequest request, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var commonName = request.CommonName?.Trim() ?? string.Empty;
        if (!CommonNameValidator.TryValidate(commonName, out var cnError))
            throw new LocalIssuanceValidationException(cnError);

        var easyRsaPath = easyRsaPathResolver.GetEasyRsaPath();
        var certificates = await easyRsaService.GetAllCertificateInfoInIndexFileAsync(easyRsaPath, cancellationToken);
        if (certificates.Any(c => string.Equals(c.CommonName, commonName, StringComparison.Ordinal) && IsActive(c))
            || File.Exists(Path.Combine(easyRsaPath, "pki", "issued", $"{commonName}.crt")))
            throw new LocalIssuanceValidationException(
                $"A valid certificate with CommonName '{commonName}' already exists on this node.", isConflict: true);

        var remote = ResolveRemote(request.Remote);
        var port = request.Port ?? configuration.GetValue("PORT", 1194);
        if (port is < 1 or > 65535)
            throw new LocalIssuanceValidationException($"Port {port} is out of range.");
        var expireDays = request.ExpireDays ?? 365;
        if (expireDays is < 1 or > 36500)
            throw new LocalIssuanceValidationException("ExpireDays must be between 1 and 36500.");

        var result = await ovpnFileService.IssueOvpnFile(new OvpnIssueRequest
        {
            EasyRsaPath = easyRsaPath,
            CommonName = commonName,
            FriendlyName = commonName,
            ConfigTemplate = LoadTemplate(),
            ServerIp = remote,
            ServerPort = port,
            IssuedTo = request.IsServiceProfile ? IssuedToLocalService : IssuedToLocalAdmin,
            CertExpireDays = expireDays,
            IsServiceProfile = request.IsServiceProfile,
            StaticIp = request.StaticIp,
        }, cancellationToken);

        var now = timeProvider.GetUtcNow();
        var record = new LocalProfileRecord
        {
            CommonName = commonName,
            SerialNumber = result.Certificate.SerialNumber,
            CertFingerprintSha256 = result.CertFingerprintSha256,
            IsServiceProfile = request.IsServiceProfile,
            StaticIp = string.IsNullOrWhiteSpace(request.StaticIp) ? null : request.StaticIp.Trim(),
            IssuedAt = now,
            ExpiresAt = result.Certificate.ExpiryDate == default ? null : result.Certificate.ExpiryDate,
            IssuedTo = result.Metadata.IssuedTo,
            FileName = result.Metadata.FileName,
            FilePath = result.Metadata.FilePath,
            CertFilePath = result.Metadata.CertFilePath,
            KeyFilePath = result.Metadata.KeyFilePath,
        };
        registry.Upsert(record);
        await outbox.EnqueueAsync(ToEvent(record, LocalIssuanceEventTypes.Issued, now), cancellationToken);

        logger.LogInformation(
            "Locally issued {CommonName} (serial {Serial}, service={IsService}, static IP {StaticIp}).",
            commonName, record.SerialNumber, record.IsServiceProfile, record.StaticIp ?? "-");
        return ToView(record, "active");
    }

    public async Task<LocalProfileView> RevokeAsync(string commonName, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var record = registry.FindActive(commonName)
                     ?? throw new LocalIssuanceValidationException(
                         $"'{commonName}' is not an active locally issued profile. Profiles issued from the dashboard are revoked there.");

        try
        {
            await ovpnFileService.RevokeOvpnFile(
                easyRsaPathResolver.GetEasyRsaPath(), commonName, record.FileName, record.FilePath, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            // Already revoked elsewhere (e.g. from the dashboard): still record it and tell the backend.
            logger.LogWarning("Certificate for {CommonName} is already gone from pki/issued; recording revoke.", commonName);
        }

        var now = timeProvider.GetUtcNow();
        record.RevokedAt = now;
        registry.Upsert(record);
        await outbox.EnqueueAsync(ToEvent(record, LocalIssuanceEventTypes.Revoked, now), cancellationToken);
        logger.LogInformation("Locally revoked {CommonName} (serial {Serial}).", commonName, record.SerialNumber);
        return ToView(record, "revoked");
    }

    public async Task<IReadOnlyList<LocalProfileView>> ListAsync(CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var certificates = await easyRsaService.GetAllCertificateInfoInIndexFileAsync(
            easyRsaPathResolver.GetEasyRsaPath(), cancellationToken);
        var local = registry.List().ToDictionary(r => r.SerialNumber, StringComparer.OrdinalIgnoreCase);

        var views = new List<LocalProfileView>();
        foreach (var cert in certificates.Where(c => !string.Equals(c.CommonName, "server", StringComparison.Ordinal)))
        {
            if (local.Remove(cert.SerialNumber, out var record))
            {
                views.Add(ToView(record, StatusOf(cert)));
                continue;
            }

            views.Add(new LocalProfileView
            {
                CommonName = cert.CommonName,
                Origin = "dashboard",
                Status = StatusOf(cert),
                SerialNumber = cert.SerialNumber,
                ExpiresAt = cert.ExpiryDate == default ? null : cert.ExpiryDate,
                RevokedAt = cert.RevokeDate,
            });
        }

        // Registry entries whose cert is missing from index.txt (should not happen; surface rather than hide).
        views.AddRange(local.Values.Select(r => ToView(r, r.RevokedAt is null ? "unknown" : "revoked")));
        return views.OrderBy(v => v.CommonName, StringComparer.Ordinal).ToList();
    }

    public async Task<string> GetOvpnAsync(string commonName, CancellationToken cancellationToken)
    {
        EnsureEnabled();
        var record = registry.FindActive(commonName)
                     ?? throw new LocalIssuanceValidationException($"'{commonName}' is not an active locally issued profile.");
        if (!File.Exists(record.FilePath))
            throw new LocalIssuanceValidationException($".ovpn for '{commonName}' is missing at {record.FilePath}.");
        return await File.ReadAllTextAsync(record.FilePath, cancellationToken);
    }

    private void EnsureEnabled()
    {
        if (!options.Enabled)
            throw new LocalIssuanceValidationException($"Local issuance is disabled ({LocalIssuanceOptions.EnabledKey}=true).");
    }

    private string ResolveRemote(string? requested)
    {
        var remote = string.IsNullOrWhiteSpace(requested)
            ? VpnServerAnnounceApiUrlResolver.GetConfiguredPublicIp(configuration)
            : requested.Trim();
        if (string.IsNullOrWhiteSpace(remote))
            throw new LocalIssuanceValidationException("No remote host: pass --remote <host> or set PUBLIC_IP on the node.");
        if (!RemoteHostPattern().IsMatch(remote))
            throw new LocalIssuanceValidationException($"Remote '{remote}' is not a valid host name or IP address.");
        return remote;
    }

    private string LoadTemplate() =>
        File.Exists(options.ClientTemplatePath) ? File.ReadAllText(options.ClientTemplatePath) : DefaultClientTemplate;

    private static bool IsActive(ServerCertificate certificate) =>
        !certificate.IsRevoked && certificate.Status == CertificateStatus.Active;

    private static string StatusOf(ServerCertificate certificate) => certificate.IsRevoked
        ? "revoked"
        : certificate.Status switch
        {
            CertificateStatus.Active => "active",
            CertificateStatus.Revoked => "revoked",
            CertificateStatus.Expired => "expired",
            _ => "unknown"
        };

    private static LocalProfileView ToView(LocalProfileRecord record, string status) => new()
    {
        CommonName = record.CommonName,
        Origin = "local",
        Status = status,
        SerialNumber = record.SerialNumber,
        CertFingerprintSha256 = record.CertFingerprintSha256,
        IsServiceProfile = record.IsServiceProfile,
        StaticIp = record.StaticIp,
        IssuedAt = record.IssuedAt,
        ExpiresAt = record.ExpiresAt,
        RevokedAt = record.RevokedAt,
    };

    private static LocalIssuanceEvent ToEvent(LocalProfileRecord record, string eventType, DateTimeOffset now) => new()
    {
        EventType = eventType,
        CommonName = record.CommonName,
        SerialNumber = record.SerialNumber,
        CertFingerprintSha256 = record.CertFingerprintSha256,
        IsServiceProfile = record.IsServiceProfile,
        StaticIp = record.StaticIp,
        OccurredAt = now,
        ExpiresAt = record.ExpiresAt,
        IssuedTo = record.IssuedTo,
        FileName = record.FileName,
        FilePath = record.FilePath,
        CertFilePath = record.CertFilePath,
        KeyFilePath = record.KeyFilePath,
    };
}
