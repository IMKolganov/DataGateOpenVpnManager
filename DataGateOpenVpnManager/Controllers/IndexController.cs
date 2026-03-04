using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using OpenVPNGateMonitor.SharedModels.DataGateOpenVpnManager.Info;

namespace DataGateOpenVpnManager.Controllers;

[ApiController]
[Route("api/info")]
public class IndexController(
    IConfiguration config,
    IWebHostEnvironment env,
    ILogger<IndexController> logger)
    : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<RootInfoResponse>> Get(CancellationToken cancellationToken)
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "Unknown version";
            var response = new RootInfoResponse
            {
                Version = version,
                Environment = env.EnvironmentName,
                Application = "DataGateOpenVpnManager",
                Description = "This service manages OpenVPN certificates and provides a JSON API for operations like create/revoke.",
                Config = new ConfigInfoResponse
                {
                    Dns1 = config["DNS1"],
                    Dns2 = config["DNS2"],
                    VpnSubnet = config["VPN_SUBNET"],
                    VpnNetmask = config["VPN_NETMASK"],
                    EasyRsaPath = config["EASY_RSA_PATH"],
                    DataDir = config["DATA_DIR"],
                    Port = config["PORT"],
                    ApiPort = config["API_PORT"],
                    Proto = config["PROTO"],
                    OpenVpnManagement = new OpenVpnManagementInfoResponse
                    {
                        Host = config["OpenVpnManagement:Host"],
                        Port = config["OpenVpnManagement:Port"]
                    },
                    BackendBaseUrl = config["BACKEND__BASEURL"]
                }
            };
            return Ok(response);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting info");
            return BadRequest(new { error = ex.Message });
        }
    }
}
