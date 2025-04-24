namespace DataGateCertManager.Models;

public class AddOvpnFileRequest
{
    public string CommonName { get; set; } = string.Empty;
    public int OvpnFileExpireDays { get; set; } = 365;
}