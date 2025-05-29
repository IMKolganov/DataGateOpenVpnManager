using DataGateCertManager.Services.OpenVpnTelnet;
using Microsoft.AspNetCore.SignalR;

namespace DataGateCertManager.Hubs;

public class OpenVpnSignalHub(OpenVpnManagementSignalService vpnService) : Hub
{
    public async Task GetStatus()
    {
        var result = await vpnService.SendStatusCommandAsync(Context.ConnectionAborted);
        await Clients.Caller.SendAsync("ReceiveStatus", result);
    }
}