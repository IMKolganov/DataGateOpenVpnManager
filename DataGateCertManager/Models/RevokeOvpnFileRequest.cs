namespace DataGateCertManager.Models;
public class RevokeOvpnFileRequest
{
    public string CommonName { get; set; } = string.Empty;
    public string OvpnFileName { get; set; } = string.Empty;
    public string OvpnFilePath { get; set; } = string.Empty;
}