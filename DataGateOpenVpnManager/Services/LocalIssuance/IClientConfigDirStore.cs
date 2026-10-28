namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>Per-CN files in OpenVPN <c>client-config-dir</c> (static addresses via ifconfig-push).</summary>
public interface IClientConfigDirStore
{
    bool IsEnabled { get; }

    /// <summary>ccd-exclusive: a CN without a ccd file cannot connect, so every issued CN needs one.</summary>
    bool IsExclusive { get; }

    /// <summary>Serialises validate + write so two issuances cannot take the same address.</summary>
    Task<IAsyncDisposable> AcquireAsync(CancellationToken cancellationToken);

    /// <summary>Throws <see cref="LocalIssuanceValidationException"/> when the address cannot be used by <paramref name="commonName"/>.</summary>
    Task ValidateStaticIpAsync(string commonName, string staticIp, CancellationToken cancellationToken);

    Task WriteAsync(string commonName, string? staticIp, CancellationToken cancellationToken);

    void Remove(string commonName);

    /// <summary>CN → ifconfig-push address for every ccd file that pins one.</summary>
    IReadOnlyDictionary<string, string> ReadStaticAssignments();
}
