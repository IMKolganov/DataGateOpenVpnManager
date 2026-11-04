using DataGateMonitor.SharedModels.DataGateOpenVpnManager.LocalIssuance.Responses;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using DataGateMonitor.SharedModels.Enums;
using DataGateOpenVpnManager.Helpers;
using DataGateOpenVpnManager.Services.EasyRsaServices.Interfaces;
using DataGateOpenVpnManager.Services.Interfaces;
using DataGateOpenVpnManager.Services.Proxy;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

public interface IServiceClientLookup
{
    Task<ServiceClientView?> FindAsync(string virtualIp, CancellationToken cancellationToken);
}

/// <summary>No writes or log-file polling. Never answer from the last successful but now stale status.</summary>
public sealed class ServiceClientLookup(
    IOpenVpnManagementStatusCache status,
    ILocalProfileRegistry registry,
    IEasyRsaParseDbService index,
    IEasyRsaPkiMutex pkiMutex,
    IEasyRsaPathResolver paths,
    ILogger<ServiceClientLookup> logger) : IServiceClientLookup
{
    public async Task<ServiceClientView?> FindAsync(string virtualIp, CancellationToken cancellationToken)
    {
        if (!IPAddress.TryParse(virtualIp, out var ip))
            return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ServiceClientLookupOptions.RequestTimeout);
        try
        {
            var started = DateTime.UtcNow;
            await status.RefreshAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            var snapshot = status.GetSnapshot();
            if (snapshot is not { IsValid: true } || snapshot.FetchedAtUtc < started)
                return null;

            var clients = snapshot.Clients.Where(c =>
                IPAddress.TryParse(c.VirtualAddress, out var address) && address.Equals(ip)).ToList();
            if (clients.Count != 1)
                return null;
            var client = clients[0];
            var record = registry.FindActive(client.CommonName);
            if (record is not { IsServiceProfile: true } || client.ConnectedSinceUnix <= 0)
                return null;
            var connectedSince = DateTimeOffset.FromUnixTimeSeconds(client.ConnectedSinceUnix);
            // status 3 has no certificate serial: never attribute a surviving older session to a reissued CN.
            if (client.ConnectedSinceUnix < record.IssuedAt.ToUnixTimeSeconds())
                return null;

            var easyRsaPath = paths.GetEasyRsaPath();
            // Read under the existing PKI gate, without EasyRsaService's initialization/key-generation fallback.
            await using var pkiLease = await pkiMutex.AcquireAsync(easyRsaPath, timeout.Token);
            var certificates = await index.ParseCertificateInfoInIndexFileAsync(Path.Combine(easyRsaPath, "pki"), timeout.Token);
            var active = certificates.Where(c => c.CommonName == client.CommonName
                && !c.IsRevoked && c.Status == CertificateStatus.Active
                && (c.ExpiryDate == default || c.ExpiryDate > DateTimeOffset.UtcNow)).ToList();
            if (active.Count != 1 || !CertificateSerial.AreEqual(active[0].SerialNumber, record.SerialNumber))
                return null;

            // Read CRL on every request too: direct CRL updates must hide even a still-connected client.
            // RemoveEntry only inspects/modifies this disposable in-memory builder; it never writes the CRL.
            var crl = CertificateRevocationListBuilder.LoadPem(
                await File.ReadAllTextAsync(Path.Combine(easyRsaPath, "pki", "crl.pem"), timeout.Token), out _);
            var serial = CertificateSerial.Normalize(record.SerialNumber);
            if (serial.Length % 2 != 0)
                serial = "0" + serial;
            var serialBytes = Convert.FromHexString(serial);
            // CRL entries retain DER INTEGER's sign byte; EasyRSA's hex serial is unsigned.
            if (serialBytes.Length > 0 && (serialBytes[0] & 0x80) != 0)
                serialBytes = [0, .. serialBytes];
            if (crl.RemoveEntry(serialBytes))
                return null;

            timeout.Token.ThrowIfCancellationRequested();
            return new ServiceClientView(client.CommonName, record.SerialNumber, client.VirtualAddress,
                client.RealAddress, connectedSince);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Service client lookup unavailable");
            return null;
        }
    }
}
