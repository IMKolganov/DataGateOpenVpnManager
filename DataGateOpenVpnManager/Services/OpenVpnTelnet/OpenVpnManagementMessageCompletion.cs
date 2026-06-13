namespace DataGateOpenVpnManager.Services.OpenVpnTelnet;

/// <summary>
/// Detects when an OpenVPN management-interface response is complete.
/// Multiline command output ends with <c>END</c> on its own line — not as a substring inside help text.
/// </summary>
public static class OpenVpnManagementMessageCompletion
{
    public static bool IsComplete(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        if (message.Contains("SUCCESS:", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("ERROR:", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("NOTIFY:", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("NOTICE:", StringComparison.OrdinalIgnoreCase))
            return true;

        return EndsWithEndLine(message);
    }

    /// <summary>True when the last non-empty line is exactly <c>END</c> (protocol terminator).</summary>
    public static bool EndsWithEndLine(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var lines = message.Split('\n');
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = lines[i].Trim('\r', ' ', '\t');
            if (line.Length == 0)
                continue;

            return line.Equals("END", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
