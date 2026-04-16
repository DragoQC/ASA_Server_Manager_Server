using AsaServerManager.Web.Contracts.Api.Admin;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class ServerController(ManagerService managerService, AuthService authService, RconService rconService, GameConfigService gameConfigService) : ControllerBase
{
	private readonly ManagerService _managerService = managerService;
	private readonly AuthService _authService = authService;
	private readonly RconService _rconService = rconService;
	private readonly GameConfigService _gameConfigService = gameConfigService;

	[HttpPost("start")]
	public async Task<IActionResult> Start(CancellationToken cancellationToken)
	{
		string? apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
		bool isValid = await _authService.IsApiKeyValidAsync(apiKey, cancellationToken);
		if (!isValid)
		{
			return Unauthorized(new
			{
				success = false,
				message = "Invalid API key."
			});
		}

		string message = await _managerService.StartAsync(cancellationToken);
		return Ok(new
		{
			success = true,
			message,
			state = _managerService.CurrentStatus.DisplayText
		});
	}

	[HttpPost("stop")]
	public async Task<IActionResult> Stop(CancellationToken cancellationToken)
	{
		string? apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
		bool isValid = await _authService.IsApiKeyValidAsync(apiKey, cancellationToken);
		if (!isValid)
		{
			return Unauthorized(new
			{
				success = false,
				message = "Invalid API key."
			});
		}

		string message = await _managerService.StopAsync(cancellationToken);
		return Ok(new
		{
			success = true,
			message,
			state = _managerService.CurrentStatus.DisplayText
		});
	}

	[HttpPost("rcon")]
	public async Task<IActionResult> ExecuteRcon([FromBody] ExecuteRconCommandRequest request, CancellationToken cancellationToken)
	{
		string? apiKey = Request.Headers["X-Api-Key"].FirstOrDefault();
		bool isValid = await _authService.IsApiKeyValidAsync(apiKey, cancellationToken);
		if (!isValid)
		{
			return Unauthorized(new
			{
				success = false,
				message = "Invalid API key."
			});
		}

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

		AsaServerManager.Web.Models.Rcon.RconSettings rconSettings = await _rconService.GetSettingsAsync(cancellationToken);
		AsaServerManager.Web.Models.Rcon.RconStatus rconStatus = await _rconService.GetStatusAsync(cancellationToken);
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
