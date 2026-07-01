namespace DataGateOpenVpnManager.Models;

public sealed class PiHoleOptions
{
    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://127.0.0.1:8080";

    public string AppPassword { get; set; } = string.Empty;

    public int PollIntervalSeconds { get; set; } = 60;

    public int BatchSize { get; set; } = 200;

    public int LookbackSeconds { get; set; } = 120;

  /// <summary>Only collect queries from client IPs starting with this prefix (e.g. 10.51.30.).</summary>
    public string ClientSubnetPrefix { get; set; } = string.Empty;
}
