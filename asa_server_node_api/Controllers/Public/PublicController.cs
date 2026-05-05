using asa_server_node_api.Models.Players;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace asa_server_node_api.Controllers.Public;

[ApiController]
[Route("api")]
public sealed class PublicController(
    ManagerService managerService,
    PlayerCountMonitorService playerCountMonitorService) : ControllerBase
{
    private readonly ManagerService _managerService = managerService;
    private readonly PlayerCountMonitorService _playerCountMonitorService = playerCountMonitorService;

    [HttpGet("state")]
    public async Task<IActionResult> State(CancellationToken cancellationToken)
    {
        await _managerService.RefreshAsync(cancellationToken);
        PlayerCountSnapshot playerSnapshot = _playerCountMonitorService.GetSnapshot();

        return Ok(new
        {
            success = true,
            activeState = _managerService.CurrentStatus.ActiveState,
            subState = _managerService.CurrentStatus.SubState,
            result = _managerService.CurrentStatus.Result,
            displayText = _managerService.CurrentStatus.DisplayText,
            canStart = _managerService.CurrentStatus.CanStart,
            canStop = _managerService.CurrentStatus.CanStop,
            uptime = _managerService.CurrentStatus.UptimeText,
            currentPlayers = playerSnapshot.CurrentPlayers,
            players = playerSnapshot.Players
        });
    }
}
