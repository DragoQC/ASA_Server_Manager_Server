using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

[ApiController]
[Route("api")]
public sealed class PublicController(ServerConfigService serverConfigService, AsaManagerService asaManagerService) : ControllerBase
{
    private readonly ServerConfigService _serverConfigService = serverConfigService;
    private readonly AsaManagerService _asaManagerService = asaManagerService;

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
        await _asaManagerService.RefreshAsync(cancellationToken);

        return Ok(new
        {
            success = true,
            activeState = _asaManagerService.CurrentStatus.ActiveState,
            subState = _asaManagerService.CurrentStatus.SubState,
            result = _asaManagerService.CurrentStatus.Result,
            displayText = _asaManagerService.CurrentStatus.DisplayText,
            canStart = _asaManagerService.CurrentStatus.CanStart,
            canStop = _asaManagerService.CurrentStatus.CanStop,
            uptime = _asaManagerService.CurrentStatus.UptimeText
        });
    }
}
