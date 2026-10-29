using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace DataGateOpenVpnManager.Helpers;

/// <summary>
/// Compares easy-rsa serials across sources: index.txt and <c>openssl x509 -serial</c> agree (upper-case hex,
/// 30 chars when easy-rsa 3.1.x drew a leading zero byte), while .NET keeps the DER sign byte ("00F5…").
/// </summary>
public static class CertificateSerial
{
    public static string Normalize(string serial) =>
        serial.Replace(":", string.Empty, StringComparison.Ordinal).Trim().TrimStart('0').ToUpperInvariant();

    public static bool AreEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
                                         && string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    /// <summary>Normalized serial of a PEM certificate file; null when it is missing or unreadable.</summary>
    public static string? TryReadFromFile(string certificatePath)
    {
        try
        {
            if (!File.Exists(certificatePath))
                return null;
            using var certificate = X509Certificate2.CreateFromPem(File.ReadAllText(certificatePath));
            return Normalize(certificate.SerialNumber);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
