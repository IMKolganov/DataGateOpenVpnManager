namespace DataGateOpenVpnManager.Models;

public sealed class EasyRsaOptions
{
    public string MainPath { get; set; } = string.Empty;

    public string IndexFileName { get; set; } = "index.txt";

    public string TaKeyFileName { get; set; } = "ta.key";
}
