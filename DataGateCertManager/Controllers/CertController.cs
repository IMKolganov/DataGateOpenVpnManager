using Microsoft.AspNetCore.Mvc;

namespace DataGateCertManager.Controllers;

[ApiController]
[Route("[controller]")]
public class CertController : ControllerBase
{
    public CertController()
    {
    }

    [HttpGet(Name = "healthcheck")]
    public IActionResult Healthcheck()
    {
        return Ok(200);
    }
}