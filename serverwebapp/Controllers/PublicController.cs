using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class PublicController(ServerConfigService serverConfigService, ManagerService managerService) : ControllerBase
{
    private readonly ServerConfigService _serverConfigService = serverConfigService;
    private readonly ManagerService _managerService = managerService;

    [HttpGet("mods")]
    public async Task<IActionResult> GetMods(CancellationToken cancellationToken)
    {
        List<string> modIds = await _serverConfigService.LoadModIdsAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            modIds
        });
    }

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
