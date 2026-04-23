using asa_server_node_api.Contracts.Api.Admin;
using asa_server_node_api.Infrastructure.Auth;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace asa_server_node_api.Controllers;

[ApiController]
[RequireApiKey]
[Route("api/admin/server")]
public sealed class ServerController(
    UploadedFileService uploadedFileService,
    ServerConfigService serverConfigService,
    InstallStateService installStateService) : ControllerBase
{
    private readonly UploadedFileService _uploadedFileService = uploadedFileService;
    private readonly ServerConfigService _serverConfigService = serverConfigService;
    private readonly InstallStateService _installStateService = installStateService;

    [HttpPost("env")]
    public async Task<IActionResult> UploadEnv([FromForm] UploadConfigFileRequest request, CancellationToken cancellationToken)
    {
        UploadFileResult result = await _uploadedFileService.UploadServerConfigAsync(request.File, cancellationToken);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("update-cluster-id")]
    public async Task<IActionResult> UpdateClusterId([FromBody] UpdateClusterIdRequest request, CancellationToken cancellationToken)
    {
        try
        {
            string clusterId = await _serverConfigService.UpdateClusterIdAsync(request.ClusterId, cancellationToken);
            string restartMessage = await _installStateService.RestartAsaIfRunningAsync(cancellationToken);

            return Ok(new
            {
                success = true,
                message = string.IsNullOrWhiteSpace(clusterId)
                    ? $"Cluster ID cleared. {restartMessage}"
                    : $"Cluster ID updated to {clusterId}. {restartMessage}",
                path = Constants.ServerConfigConstants.EnvFilePath,
                clusterId
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }
}
