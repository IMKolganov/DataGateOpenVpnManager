using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Services.PiHole;

public interface IPiHoleRuntimeOptionsStore
{
    PiHoleOptions GetEffective();

    void Apply(PiHoleOptions options);
}

public sealed class PiHoleRuntimeOptionsStore(IOptionsMonitor<PiHoleOptions> optionsMonitor) : IPiHoleRuntimeOptionsStore
{
    private PiHoleOptions? _override;
    private readonly object _sync = new();

    public PiHoleOptions GetEffective()
    {
        lock (_sync)
        {
            if (_override is not null)
                return Clone(_override);

            return Clone(optionsMonitor.CurrentValue);
        }
    }

    public void Apply(PiHoleOptions options)
    {
        lock (_sync)
        {
            _override = Clone(options);
        }
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
}
