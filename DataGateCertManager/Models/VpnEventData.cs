namespace DataGateCertManager.Models;

public class VpnEventData
{
    public string? CommonName { get; set; }
    public string? RealAddress { get; set; }
    public string? VirtualAddress { get; set; }
    public string? ConnectedSince { get; set; }
}