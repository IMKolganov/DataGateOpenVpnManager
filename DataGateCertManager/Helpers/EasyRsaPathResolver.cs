namespace DataGateCertManager.Helpers;

public class EasyRsaPathResolver(IConfiguration configuration) : IEasyRsaPathResolver
{
    public string GetEasyRsaPath()
    {
        return Environment.GetEnvironmentVariable("EASY_RSA_PATH")
               ?? configuration["EasyRsa:MainPath"]
               ?? throw new InvalidOperationException("EasyRsa:MainPath is not set");
    }
}