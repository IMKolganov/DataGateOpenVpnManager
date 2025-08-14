namespace DataGateCertManager.Models;


public class VpnEnvDump
{
    public string? Hook { get; set; }
    public DateTime Timestamp { get; set; }
    public List<string>? Args { get; set; }
    public string? EnvB64 { get; set; }
}