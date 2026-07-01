using System.Text.Json;
using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Services.PiHole;

public interface IPiHoleRuntimeOptionsStore
{
    PiHoleOptions GetEffective();

    /// <summary>Apply timestamp from persisted runtime config (survives container restart).</summary>
    DateTimeOffset? PersistedAppliedAtUtc { get; }

    /// <summary>True when PIHOLE_* / PiHole__* env vars override dashboard or persisted config.</summary>
    bool HasEnvOverrides { get; }

    void Apply(PiHoleOptions options);
}

public sealed class PiHoleRuntimeOptionsStore : IPiHoleRuntimeOptionsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IOptionsMonitor<PiHoleOptions> _optionsMonitor;
    private readonly ILogger<PiHoleRuntimeOptionsStore> _logger;
    private readonly PiHoleEnvOverrides _envOverrides;
    private readonly string _path;
    private PiHoleOptions? _override;
    private DateTimeOffset? _persistedAppliedAtUtc;
    private readonly object _sync = new();

    public PiHoleRuntimeOptionsStore(
        IOptionsMonitor<PiHoleOptions> optionsMonitor,
        IConfiguration configuration,
        ILogger<PiHoleRuntimeOptionsStore> logger)
        : this(optionsMonitor, configuration, logger, loadFromDisk: true)
    {
    }

    internal PiHoleRuntimeOptionsStore(
        IOptionsMonitor<PiHoleOptions> optionsMonitor,
        IConfiguration configuration,
        ILogger<PiHoleRuntimeOptionsStore> logger,
        bool loadFromDisk)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
        _path = BuildPath(configuration);
        _envOverrides = PiHoleEnvOverrides.FromConfiguration(configuration);

        if (loadFromDisk)
            TryLoadFromDisk();
    }

    public bool HasEnvOverrides => _envOverrides.HasAny;

    public DateTimeOffset? PersistedAppliedAtUtc
    {
        get
        {
            lock (_sync)
            {
                return _persistedAppliedAtUtc;
            }
        }
    }

    public PiHoleOptions GetEffective()
    {
        lock (_sync)
        {
            var effective = Clone(_override ?? _optionsMonitor.CurrentValue);
            _envOverrides.ApplyTo(effective);
            return effective;
        }
    }

    public void Apply(PiHoleOptions options)
    {
        var appliedAt = DateTimeOffset.UtcNow;
        lock (_sync)
        {
            _override = Clone(options);
            _persistedAppliedAtUtc = appliedAt;
            SaveToDiskUnsafe(_override, appliedAt);
        }
    }

    internal static string BuildPath(IConfiguration configuration)
    {
        var dataDir = configuration["DATA_DIR"] ?? "/openvpn-tcp-wss";
        return Path.Combine(dataDir, "pihole-runtime-config.json");
    }

    private void TryLoadFromDisk()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
                return;

            try
            {
                var json = File.ReadAllText(_path);
                var record = JsonSerializer.Deserialize<PiHoleRuntimeConfigFile>(json, JsonOptions);
                if (record is null)
                    return;

                _override = record.ToOptions();
                _persistedAppliedAtUtc = record.AppliedAtUtc.ToUniversalTime();
                _logger.LogInformation(
                    "Loaded Pi-hole runtime config from {Path}. Enabled={Enabled}, BaseUrl={BaseUrl}, AppliedAtUtc={AppliedAtUtc}",
                    _path,
                    _override.Enabled,
                    _override.BaseUrl,
                    _persistedAppliedAtUtc);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load Pi-hole runtime config from {Path}; falling back to env/appsettings.", _path);
            }
        }
    }

    private void SaveToDiskUnsafe(PiHoleOptions options, DateTimeOffset appliedAtUtc)
    {
        try
        {
            var record = PiHoleRuntimeConfigFile.FromOptions(options, appliedAtUtc);
            var json = JsonSerializer.Serialize(record, JsonOptions);
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tempPath = _path + ".tmp";
            File.WriteAllText(tempPath, json);
            RestrictToOwnerOnly(tempPath);
            File.Move(tempPath, _path, overwrite: true);
            RestrictToOwnerOnly(_path);
            _logger.LogInformation("Persisted Pi-hole runtime config to {Path}.", _path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist Pi-hole runtime config to {Path}.", _path);
        }
    }

    private static void RestrictToOwnerOnly(string path)
    {
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static PiHoleOptions Clone(PiHoleOptions source) => new()
    {
        Enabled = source.Enabled,
        BaseUrl = source.BaseUrl,
        AppPassword = source.AppPassword,
        PollIntervalSeconds = source.PollIntervalSeconds,
        BatchSize = source.BatchSize,
        LookbackSeconds = source.LookbackSeconds,
        ClientSubnetPrefix = source.ClientSubnetPrefix
    };

    private sealed class PiHoleRuntimeConfigFile
    {
        public bool Enabled { get; set; }

        public string BaseUrl { get; set; } = string.Empty;

        public string AppPassword { get; set; } = string.Empty;

        public int PollIntervalSeconds { get; set; }

        public int BatchSize { get; set; }

        public int LookbackSeconds { get; set; }

        public string ClientSubnetPrefix { get; set; } = string.Empty;

        public DateTimeOffset AppliedAtUtc { get; set; }

        public static PiHoleRuntimeConfigFile FromOptions(PiHoleOptions options, DateTimeOffset appliedAtUtc) => new()
        {
            Enabled = options.Enabled,
            BaseUrl = options.BaseUrl,
            AppPassword = options.AppPassword,
            PollIntervalSeconds = options.PollIntervalSeconds,
            BatchSize = options.BatchSize,
            LookbackSeconds = options.LookbackSeconds,
            ClientSubnetPrefix = options.ClientSubnetPrefix,
            AppliedAtUtc = appliedAtUtc.ToUniversalTime()
        };

        public PiHoleOptions ToOptions() => new()
        {
            Enabled = Enabled,
            BaseUrl = BaseUrl,
            AppPassword = AppPassword,
            PollIntervalSeconds = PollIntervalSeconds > 0 ? PollIntervalSeconds : 60,
            BatchSize = BatchSize > 0 ? BatchSize : 200,
            LookbackSeconds = LookbackSeconds >= 0 ? LookbackSeconds : 120,
            ClientSubnetPrefix = ClientSubnetPrefix
        };
    }
}
