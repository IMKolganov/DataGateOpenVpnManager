namespace DataGateCertManager.Models;

public class DownloadOvpnFileRequest
{
    public string CommonName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
}