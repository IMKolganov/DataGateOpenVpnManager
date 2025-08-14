namespace DataGateCertManager.Models;
//todo: move to nuget
public sealed class VpnEventRequest
{
    public int VpnServerId { get; init; }
    public string EventType { get; init; } = default!; // "ClientConnect", "ClientDisconnect", ...

    public string? CommonName { get; init; }
    public string? RealAddress { get; init; }       // "ip:port"
    public string? VirtualAddress { get; init; }

    public DateTimeOffset? ConnectedSince { get; init; } // time_unix
    public string? ScriptType { get; init; }             // script_type
    public string? Action { get; init; }                 // learn-address action
    public DateTimeOffset? EventTimeUtc { get; init; }   // set if you have exact time

    public long? BytesReceived { get; init; }            // final (disconnect)
    public long? BytesSent { get; init; }
    public long? SampleBytesIn { get; init; }            // status 3 snapshot cumulative
    public long? SampleBytesOut { get; init; }

    public long? DurationSec { get; init; }              // time_duration
    public DateTimeOffset? DisconnectedAt { get; init; } // start + duration (optional precomputed)

    public string? IvVer { get; init; }
    public string? IvGuiVer { get; init; }
    public string? IvPlat { get; init; }

    public string? Message { get; init; }
}
