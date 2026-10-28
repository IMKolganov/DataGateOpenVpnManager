using System.Text.RegularExpressions;

namespace DataGateOpenVpnManager.Helpers;

/// <summary>
/// CN rules for locally issued profiles. The CN becomes a file name (pki/issued, ovpn_files, ccd) and an
/// easy-rsa argument, so only a conservative charset is allowed.
/// </summary>
public static partial class CommonNameValidator
{
    /// <summary>X.509 ub-common-name.</summary>
    public const int MaxLength = 64;

    private static readonly string[] Reserved = ["server", "ca", "ta", "crl"];

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex AllowedPattern();

    public static bool TryValidate(string? commonName, out string error)
    {
        if (string.IsNullOrWhiteSpace(commonName))
        {
            error = "CommonName is required.";
            return false;
        }

        if (commonName.Length > MaxLength)
        {
            error = $"CommonName must be at most {MaxLength} characters.";
            return false;
        }

        if (!AllowedPattern().IsMatch(commonName))
        {
            error = "CommonName may contain only letters, digits, '.', '_' and '-', and must start with a letter or digit.";
            return false;
        }

        if (commonName.Contains("..", StringComparison.Ordinal))
        {
            error = "CommonName must not contain '..'.";
            return false;
        }

        if (Reserved.Contains(commonName, StringComparer.OrdinalIgnoreCase))
        {
            error = $"CommonName '{commonName}' is reserved.";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
