using DataGateOpenVpnManager.Models;
using DataGateOpenVpnManager.Services.PiHole;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Tests.Services.PiHole;

internal static class PiHoleRuntimeOptionsStoreTestHelper
{
    public static PiHoleRuntimeOptionsStore Create(
        PiHoleOptions? options = null,
        string? dataDir = null,
        bool loadFromDisk = false,
        IReadOnlyDictionary<string, string?>? env = null)
    {
        var configData = new Dictionary<string, string?>();
        if (dataDir is not null)
            configData["DATA_DIR"] = dataDir;
        if (env is not null)
        {
            foreach (var (key, value) in env)
                configData[key] = value;
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();

        return new PiHoleRuntimeOptionsStore(
            new TestOptionsMonitor(options ?? new PiHoleOptions()),
            configuration,
            NullLogger<PiHoleRuntimeOptionsStore>.Instance,
            loadFromDisk);
    }

    internal sealed class TestOptionsMonitor(PiHoleOptions current) : IOptionsMonitor<PiHoleOptions>
    {
        public PiHoleOptions CurrentValue { get; } = current;
        public PiHoleOptions Get(string? name) => CurrentValue;
        public IDisposable? OnChange(Action<PiHoleOptions, string?> listener) => null;
    }
}
