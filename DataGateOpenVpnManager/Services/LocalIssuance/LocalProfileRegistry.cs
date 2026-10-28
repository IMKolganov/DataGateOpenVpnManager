using Newtonsoft.Json;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>Profile issued through the local admin channel (status is always re-read from easy-rsa index.txt).</summary>
public sealed class LocalProfileRecord
{
    public string CommonName { get; set; } = string.Empty;
    public string SerialNumber { get; set; } = string.Empty;
    public string CertFingerprintSha256 { get; set; } = string.Empty;
    public bool IsServiceProfile { get; set; }
    public string? StaticIp { get; set; }
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public string IssuedTo { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string CertFilePath { get; set; } = string.Empty;
    public string KeyFilePath { get; set; } = string.Empty;
}

public interface ILocalProfileRegistry
{
    IReadOnlyList<LocalProfileRecord> List();

    /// <summary>Latest record for <paramref name="commonName"/> that is not revoked.</summary>
    LocalProfileRecord? FindActive(string commonName);

    void Upsert(LocalProfileRecord record);
}

/// <summary>JSON file in the private local-issuance directory, keyed by serial number.</summary>
public sealed class LocalProfileRegistry(LocalIssuanceOptions options) : ILocalProfileRegistry
{
    private readonly object _sync = new();

    public IReadOnlyList<LocalProfileRecord> List()
    {
        lock (_sync)
            return Load();
    }

    public LocalProfileRecord? FindActive(string commonName)
    {
        lock (_sync)
            return Load()
                .Where(r => r.RevokedAt is null && string.Equals(r.CommonName, commonName, StringComparison.Ordinal))
                .MaxBy(r => r.IssuedAt);
    }

    public void Upsert(LocalProfileRecord record)
    {
        lock (_sync)
        {
            var records = Load();
            records.RemoveAll(r => string.Equals(r.SerialNumber, record.SerialNumber, StringComparison.OrdinalIgnoreCase));
            records.Add(record);
            PrivateFiles.EnsurePrivateDirectory(options.Directory);
            PrivateFiles.WriteAllTextAtomic(options.RegistryPath, JsonConvert.SerializeObject(records, Formatting.Indented));
        }
    }

    private List<LocalProfileRecord> Load()
    {
        if (!File.Exists(options.RegistryPath))
            return [];
        return JsonConvert.DeserializeObject<List<LocalProfileRecord>>(File.ReadAllText(options.RegistryPath)) ?? [];
    }
}
