using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class PublicController(ManagerService managerService) : ControllerBase
{
    private readonly ManagerService _managerService = managerService;

    [HttpGet("state")]
    public async Task<IActionResult> State(CancellationToken cancellationToken)
    {
        await _managerService.RefreshAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            activeState = _managerService.CurrentStatus.ActiveState,
            subState = _managerService.CurrentStatus.SubState,
            result = _managerService.CurrentStatus.Result,
            displayText = _managerService.CurrentStatus.DisplayText,
            canStart = _managerService.CurrentStatus.CanStart,
            canStop = _managerService.CurrentStatus.CanStop,
            uptime = _managerService.CurrentStatus.UptimeText
        });
    }
}
