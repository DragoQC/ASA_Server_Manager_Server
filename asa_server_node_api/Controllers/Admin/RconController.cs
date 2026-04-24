using asa_server_node_api.Contracts.Api.Admin;
using asa_server_node_api.Infrastructure.Auth;
using asa_server_node_api.Models.Rcon;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace asa_server_node_api.Controllers.Admin;

[ApiController]
[RequireApiKey]
[Route("api/admin/rcon")]
public sealed class RconController(RconService rconService, GameConfigService gameConfigService) : ControllerBase
{
    private readonly RconService _rconService = rconService;
    private readonly GameConfigService _gameConfigService = gameConfigService;

    [HttpPost]
    public async Task<IActionResult> Execute([FromBody] ExecuteRconCommandRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Command))
        {
            return BadRequest(new
            {
                success = false,
                message = "Command is required."
            });
        }

        if (!_gameConfigService.HasGameUserSettingsIniFile())
        {
            return BadRequest(new
            {
                success = false,
                state = "Missing",
                message = "GameUserSettings.ini missing."
            });
        }

        RconSettings rconSettings = await _rconService.GetSettingsAsync(cancellationToken);
        RconStatus rconStatus = await _rconService.GetStatusAsync(cancellationToken);
        if (!rconStatus.CanExecute(rconSettings))
        {
            return BadRequest(new
            {
                success = false,
                state = rconStatus.StateLabel,
                message = rconStatus.Message
            });
        }

        string response = await _rconService.ExecuteAsync(request.Command, cancellationToken);
        return Ok(new
        {
            success = true,
            command = request.Command,
            response
        });
    }
}
