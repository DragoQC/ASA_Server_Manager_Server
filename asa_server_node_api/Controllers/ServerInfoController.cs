using asa_server_node_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace asa_server_node_api.Controllers;

[ApiController]
[Route("api/server")]
public sealed class ServerInfoController(SystemMetricsService systemMetricsService) : ControllerBase
{
	private readonly SystemMetricsService _systemMetricsService = systemMetricsService;

	[HttpGet("me")]
	public async Task<IActionResult> Me(CancellationToken cancellationToken)
	{
		Models.SystemMetrics.ServerInfoSnapshot snapshot =
						await _systemMetricsService.LoadServerInfoAsync(cancellationToken);

		return Ok(new
		{
			success = true,
			serverName = snapshot.ServerName,
			mapName = snapshot.MapName,
			gamePort = snapshot.GamePort,
			maxPlayers = snapshot.MaxPlayers,
			modIds = snapshot.ModIds,
			checkedAtUtc = snapshot.CheckedAtUtc
		});
	}
}
