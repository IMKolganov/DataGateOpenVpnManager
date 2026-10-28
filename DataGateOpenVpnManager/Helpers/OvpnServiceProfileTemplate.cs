using System.Text;

namespace DataGateOpenVpnManager.Helpers;

/// <summary>
/// Turns a client .ovpn into a "service" profile: only traffic inside the VPN goes through the tunnel.
/// The server pushes redirect-gateway / DNS (see server.conf), so the client filters those pushes and
/// any local copies of the same directives are dropped.
/// </summary>
public static class OvpnServiceProfileTemplate
{
    public static readonly string[] FilteredPushes = ["redirect-gateway", "dhcp-option", "block-outside-dns"];

    public static string Apply(string ovpn)
    {
        if (string.IsNullOrWhiteSpace(ovpn))
            return ovpn;

        var newline = ovpn.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = ovpn.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var result = new List<string>(lines.Count + FilteredPushes.Length);
        string? inlineBlock = null;
        var inserted = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (inlineBlock is not null)
            {
                result.Add(line);
                if (trimmed.Equals($"</{inlineBlock}>", StringComparison.OrdinalIgnoreCase))
                    inlineBlock = null;
                continue;
            }

            if (trimmed.StartsWith('<') && trimmed.EndsWith('>') && !trimmed.StartsWith("</", StringComparison.Ordinal))
            {
                inlineBlock = trimmed[1..^1];
                result.Add(line);
                continue;
            }

            if (IsDirective(trimmed, "pull-filter") || FilteredPushes.Any(d => IsDirective(trimmed, d)))
                continue;

            result.Add(line);

            if (!inserted && IsDirective(trimmed, "client"))
            {
                result.AddRange(PullFilterLines());
                inserted = true;
            }
        }

        if (!inserted)
            result.InsertRange(0, PullFilterLines());

        var sb = new StringBuilder();
        for (var i = 0; i < result.Count; i++)
        {
            if (i > 0)
                sb.Append(newline);
            sb.Append(result[i]);
        }

        return sb.ToString();
    }

    private static IEnumerable<string> PullFilterLines() =>
        FilteredPushes.Select(d => $"pull-filter ignore \"{d}\"");

    private static bool IsDirective(string trimmedLine, string name) =>
        trimmedLine.Equals(name, StringComparison.OrdinalIgnoreCase)
        || (trimmedLine.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && trimmedLine.Length > name.Length
            && char.IsWhiteSpace(trimmedLine[name.Length]));
}
