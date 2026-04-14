using AsaServerManager.Web.Contracts.Api.Admin;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class ServerController(AsaManagerService asaManagerService, AuthService authService, RconService rconService) : ControllerBase
{
	private readonly AsaManagerService _asaManagerService = asaManagerService;
	private readonly AuthService _authService = authService;
	private readonly RconService _rconService = rconService;

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

		string message = await _asaManagerService.StartAsync(cancellationToken);
		return Ok(new
		{
			success = true,
			message,
			state = _asaManagerService.CurrentStatus.DisplayText
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

		string message = await _asaManagerService.StopAsync(cancellationToken);
		return Ok(new
		{
			success = true,
			message,
			state = _asaManagerService.CurrentStatus.DisplayText
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

		AsaServerManager.Web.Models.Rcon.RconStatus rconStatus = await _rconService.GetStatusAsync(cancellationToken);
		if (!rconStatus.CanExecute)
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
