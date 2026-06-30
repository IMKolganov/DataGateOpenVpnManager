namespace DataGateOpenVpnManager.Services.PiHole;

public interface IPiHoleQueryCursorStore
{
    DateTimeOffset? GetLastUntilUtc();

    void SaveLastUntilUtc(DateTimeOffset untilUtc);
}

public sealed class PiHoleQueryCursorStore : IPiHoleQueryCursorStore
{
    private readonly string _path;
    private readonly object _sync = new();
    private DateTimeOffset? _cached;

    public PiHoleQueryCursorStore(IConfiguration configuration)
    {
        var dataDir = configuration["DATA_DIR"] ?? "/openvpn-tcp-wss";
        _path = Path.Combine(dataDir, "pihole-query-cursor.txt");
    }

    public DateTimeOffset? GetLastUntilUtc()
    {
        lock (_sync)
        {
            if (_cached.HasValue)
                return _cached;

            if (!File.Exists(_path))
                return null;

            var text = File.ReadAllText(_path).Trim();
            if (DateTimeOffset.TryParse(text, null, System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                _cached = parsed.ToUniversalTime();
                return _cached;
            }

            return null;
        }
    }

    public void SaveLastUntilUtc(DateTimeOffset untilUtc)
    {
        var normalized = untilUtc.ToUniversalTime();
        lock (_sync)
        {
            _cached = normalized;
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, normalized.ToString("O"));
        }
    }
}
