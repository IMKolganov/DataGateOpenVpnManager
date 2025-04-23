using DataGateCertManager.Models;
using DataGateCertManager.Services.EasyRsaServices.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OvpnFileController(
    IEasyRsaService easyRsaService,
    IConfiguration configuration,
    ILogger<OvpnFileController> logger)
    : ControllerBase
{
    
}