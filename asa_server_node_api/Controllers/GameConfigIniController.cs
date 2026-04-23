using asa_server_node_api.Contracts.Api.Admin;
using asa_server_node_api.Infrastructure.Auth;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace asa_server_node_api.Controllers;

[ApiController]
[RequireApiKey]
[Route("api/admin/game-config-ini")]
public sealed class GameConfigIniController(UploadedFileService uploadedFileService) : ControllerBase
{
    private readonly UploadedFileService _uploadedFileService = uploadedFileService;

    [HttpPost("game-ini")]
    public async Task<IActionResult> UploadGameIni([FromForm] UploadConfigFileRequest request, CancellationToken cancellationToken)
    {
        UploadFileResult result = await _uploadedFileService.UploadGameIniAsync(request.File, cancellationToken);

        return result.Success ? Ok(result) : BadRequest(result);
    }

    [HttpPost("game-user-settings-ini")]
    public async Task<IActionResult> UploadGameUserSettingsIni([FromForm] UploadConfigFileRequest request, CancellationToken cancellationToken)
    {
        UploadFileResult result = await _uploadedFileService.UploadGameUserSettingsIniAsync(request.File, cancellationToken);

        return result.Success ? Ok(result) : BadRequest(result);
    }
}
