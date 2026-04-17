using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

[ApiController]
[Route("api/server")]
public sealed class ServerInfoController(SystemMetricsService systemMetricsService) : ControllerBase
{
    private readonly SystemMetricsService _systemMetricsService = systemMetricsService;

    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        AsaServerManager.Web.Models.SystemMetrics.ServerInfoSnapshot snapshot =
            await _systemMetricsService.LoadServerInfoAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            cpuTotal = snapshot.CpuTotal,
            ramTotal = snapshot.RamTotal,
            diskTotal = snapshot.DiskTotal,
            checkedAtUtc = snapshot.CheckedAtUtc
        });
    }
}
