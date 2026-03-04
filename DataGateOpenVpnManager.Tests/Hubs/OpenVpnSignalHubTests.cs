using DataGateOpenVpnManager.Hubs;
using DataGateOpenVpnManager.Services.OpenVpnTelnet;
using Microsoft.Extensions.Logging;

namespace DataGateOpenVpnManager.Tests.Hubs;

public class OpenVpnSignalHubTests
{
    [Fact]
    public void Hub_CanBeConstructed_WithServiceAndLogger()
    {
        var telnetClient = new TelnetClient("127.0.0.1", 65000, new Microsoft.Extensions.Logging.Abstractions.NullLogger<TelnetClient>());
        var commandQueueLogger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<CommandQueue>();
        var vpnService = new OpenVpnManagementSignalService(telnetClient, commandQueueLogger);
        var logger = new Microsoft.Extensions.Logging.Abstractions.NullLogger<OpenVpnSignalHub>();
        var hub = new OpenVpnSignalHub(vpnService, logger);
        Assert.NotNull(hub);
    }
}
