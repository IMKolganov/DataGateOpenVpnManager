using DataGateOpenVpnManager.Models;
using Microsoft.Extensions.Options;

namespace DataGateOpenVpnManager.Helpers;

public class EasyRsaPathResolver(IOptions<EasyRsaOptions> options) : IEasyRsaPathResolver
{
    public string GetEasyRsaPath()
    {
        var path = options.Value.MainPath;
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("EasyRsa:MainPath is not set");

        return path;
    }
}
