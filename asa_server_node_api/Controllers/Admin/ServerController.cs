using asa_server_node_api.Infrastructure.Auth;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace asa_server_node_api.Controllers.Admin;

[ApiController]
[RequireApiKey]
[Route("api/admin/server")]
public sealed class ServerController(
    ManagerService managerService,
    SystemMetricsService systemMetricsService,
    AdminHostMetricsMonitorService adminHostMetricsMonitorService,
    InstallStateService installStateService) : ControllerBase
{
    private readonly ManagerService _managerService = managerService;
    private readonly SystemMetricsService _systemMetricsService = systemMetricsService;
    private readonly AdminHostMetricsMonitorService _adminHostMetricsMonitorService = adminHostMetricsMonitorService;
    private readonly InstallStateService _installStateService = installStateService;

    [HttpGet]
    public async Task<IActionResult> Info(CancellationToken cancellationToken)
    {
        Models.SystemMetrics.ServerInfoSnapshot serverInfo =
            await _systemMetricsService.LoadServerInfoAsync(cancellationToken);
        Models.Admin.AdminHostMetricsSnapshot metrics = _adminHostMetricsMonitorService.GetSnapshot();

        return Ok(new
        {
            success = true,
            serverName = serverInfo.ServerName,
            mapName = serverInfo.MapName,
            gamePort = serverInfo.GamePort,
            maxPlayers = serverInfo.MaxPlayers,
            modIds = serverInfo.ModIds,
            totalRam = metrics.RamTotal,
            ramPercentage = metrics.RamPercentage,
            cpuUsage = metrics.CpuUsage,
            diskTotal = metrics.DiskTotal,
            diskUsed = metrics.DiskUsed,
            checkedAtUtc = metrics.CheckedAtUtc
        });
    }

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

    [HttpPost("install-all")]
    public async Task<IActionResult> InstallAll(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _installStateService.InstallAllAsync(cancellationToken));
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new
            {
                success = false,
                message = exception.Message
            });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new
            {
                success = false,
                message = exception.Message
            });
        }
    }
}
