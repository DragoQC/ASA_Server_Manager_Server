using asa_server_node_api.Infrastructure.Auth;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace asa_server_node_api.Controllers;

[ApiController]
[RequireApiKey]
[Route("api/admin/manager")]
public sealed class ManagerController(ManagerService managerService) : ControllerBase
{
    private readonly ManagerService _managerService = managerService;

    [HttpPost("start")]
    public async Task<IActionResult> Start(CancellationToken cancellationToken)
    {
        try
        {
            string message = await _managerService.StartAsync(cancellationToken);
            return Ok(new
            {
                success = true,
                message,
                state = _managerService.CurrentStatus.DisplayText
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                success = false,
                message = exception.Message,
                state = _managerService.CurrentStatus.DisplayText
            });
        }
    }

    [HttpPost("stop")]
    public async Task<IActionResult> Stop(CancellationToken cancellationToken)
    {
        try
        {
            string message = await _managerService.StopAsync(cancellationToken);
            return Ok(new
            {
                success = true,
                message,
                state = _managerService.CurrentStatus.DisplayText
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                success = false,
                message = exception.Message,
                state = _managerService.CurrentStatus.DisplayText
            });
        }
    }

    [HttpPost("restart")]
    public async Task<IActionResult> Restart(CancellationToken cancellationToken)
    {
        try
        {
            string message = await _managerService.RestartAsync(cancellationToken);
            return Ok(new
            {
                success = true,
                message,
                state = _managerService.CurrentStatus.DisplayText
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                success = false,
                message = exception.Message,
                state = _managerService.CurrentStatus.DisplayText
            });
        }
    }
}
