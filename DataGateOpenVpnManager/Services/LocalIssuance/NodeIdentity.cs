using System.Security.Cryptography;
using System.Text;

namespace DataGateOpenVpnManager.Services.LocalIssuance;

/// <summary>
/// Node key pair (ECDSA P-256) used to sign node → backend events. Only the public key leaves the node:
/// it rides along with the discovery announce and is bound to the VpnServer when the admin approves it,
/// so no shared secret has to be delivered to a node the backend may not be able to reach.
/// </summary>
public interface INodeIdentity
{
    string PublicKeyPem { get; }

    /// <summary>Lower-case hex SHA-256 of the SubjectPublicKeyInfo DER; also the key id header.</summary>
    string Fingerprint { get; }

    byte[] Sign(byte[] data);
}

public sealed class NodeIdentity : INodeIdentity, IDisposable
{
    private readonly ECDsa _key;

    public NodeIdentity(LocalIssuanceOptions options, ILogger<NodeIdentity> logger)
    {
        PrivateFiles.EnsurePrivateDirectory(options.Directory);
        _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        if (File.Exists(options.NodeKeyPath))
        {
            _key.ImportFromPem(File.ReadAllText(options.NodeKeyPath));
            PrivateFiles.RestrictToOwner(options.NodeKeyPath);
        }
        else
        {
            PrivateFiles.WriteAllTextAtomic(options.NodeKeyPath, _key.ExportPkcs8PrivateKeyPem());
            logger.LogInformation("Generated node identity key at {Path}", options.NodeKeyPath);
        }

        PublicKeyPem = _key.ExportSubjectPublicKeyInfoPem();
        Fingerprint = FingerprintOf(_key.ExportSubjectPublicKeyInfo());
        logger.LogInformation("Node identity fingerprint: {Fingerprint}", Fingerprint);
    }

    public string PublicKeyPem { get; }
    public string Fingerprint { get; }

    public byte[] Sign(byte[] data) => _key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public static string FingerprintOf(byte[] subjectPublicKeyInfo) =>
        Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));

    public void Dispose() => _key.Dispose();
}

/// <summary>
/// Request signing for node → backend calls. Must match the backend verifier byte for byte:
/// <c>"DGN1\n{unixSeconds}\n{METHOD}\n{path}\n{hex(sha256(body))}"</c>, ECDSA P-256 / SHA-256, IEEE P1363, base64.
/// </summary>
public static class NodeRequestSignature
{
    public const string KeyIdHeader = "X-DataGate-Node-Key";
    public const string TimestampHeader = "X-DataGate-Timestamp";
    public const string SignatureHeader = "X-DataGate-Signature";

    public static byte[] CanonicalBytes(long unixSeconds, string method, string path, byte[] body) =>
        Encoding.UTF8.GetBytes(
            $"DGN1\n{unixSeconds}\n{method.ToUpperInvariant()}\n{path}\n{Convert.ToHexStringLower(SHA256.HashData(body))}");

    /// <param name="signedPath">Backend route (e.g. <c>/api/open-vpn-servers/local-issuance-events</c>), not the URL path,
    /// so a path prefix on a reverse proxy in front of the backend does not break verification.</param>
    public static void Apply(HttpRequestMessage request, INodeIdentity identity, byte[] body, DateTimeOffset now, string signedPath)
    {
        var timestamp = now.ToUnixTimeSeconds();
        var path = "/" + signedPath.TrimStart('/');
        var signature = identity.Sign(CanonicalBytes(timestamp, request.Method.Method, path, body));
        request.Headers.Add(KeyIdHeader, identity.Fingerprint);
        request.Headers.Add(TimestampHeader, timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture));
        request.Headers.Add(SignatureHeader, Convert.ToBase64String(signature));
    }
}
