namespace DataGateOpenVpnManager.Helpers;

/// <summary>
/// Backend URL for dashboard calls. A missing or unusable value must not fail host startup:
/// the node keeps running, and registration / outbox delivery wait until it is set.
/// </summary>
internal static class BackendBaseAddress
{
    public const string ConfigurationKey = "Backend:BaseUrl";

    public const string MissingMessage =
        "Backend:BaseUrl is not configured. The node will keep running; discovery registration and backend notifications stay pending until Backend__BaseUrl is set to an absolute http(s) URL and the container is restarted.";

    private static int _logged;

    /// <summary>
    /// Absolute http(s) URI with a trailing slash, or null when the setting is missing or unusable.
    /// Logs the startup error once.
    /// </summary>
    public static Uri? TryResolve(IConfiguration config)
    {
        var raw = config[ConfigurationKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            LogOnce(MissingMessage);
            return null;
        }

        var trimmed = raw.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            LogOnce(
                $"Backend:BaseUrl '{trimmed}' is not an absolute http(s) URL. The node will keep running; discovery registration and backend notifications stay pending until Backend__BaseUrl is a valid URL and the container is restarted.");
            return null;
        }

        return new Uri(VpnServerAnnounceApiUrlResolver.EnsureTrailingSlash(uri.AbsoluteUri));
    }

    private static void LogOnce(string message)
    {
        if (Interlocked.Exchange(ref _logged, 1) != 0)
            return;

        Console.Error.WriteLine("[DataGate] " + message);
    }
}
