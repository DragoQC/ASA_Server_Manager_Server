using asa_server_node_api.Contracts.Api.Admin;
using asa_server_node_api.Infrastructure.Auth;
using asa_server_node_api.Services;
using Microsoft.AspNetCore.Mvc;

namespace asa_server_node_api.Controllers.Admin;

[ApiController]
[RequireApiKey]
[Route("api/admin/server-config")]
public sealed class ServerConfigController(
    UploadedFileService uploadedFileService,
    GameConfigService gameConfigService,
    InstallStateService installStateService) : ControllerBase
{
    private readonly UploadedFileService _uploadedFileService = uploadedFileService;
    private readonly GameConfigService _gameConfigService = gameConfigService;
    private readonly InstallStateService _installStateService = installStateService;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _installStateService.LoadExistingServerConfigAsync(cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    [HttpGet("game-ini")]
    public Task<IActionResult> GetGameIni(CancellationToken cancellationToken)
    {
        return LoadGameConfigAsync(
            Constants.GameConfigConstants.GameIniPath,
            "Game.ini",
            cancellationToken);
    }

    [HttpGet("game-user-settings-ini")]
    public Task<IActionResult> GetGameUserSettingsIni(CancellationToken cancellationToken)
    {
        return LoadGameConfigAsync(
            Constants.GameConfigConstants.GameUserSettingsIniPath,
            "GameUserSettings.ini",
            cancellationToken);
    }

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

    [HttpPatch]
    public async Task<IActionResult> Patch([FromBody] PatchServerConfigRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _installStateService.PatchServerConfigAsync(request, cancellationToken));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }

    private async Task<IActionResult> LoadGameConfigAsync(string filePath, string fileName, CancellationToken cancellationToken)
    {
        try
        {
            string content = await _gameConfigService.LoadEditorContentAsync(filePath, cancellationToken);

            return Ok(new
            {
                success = true,
                fileName,
                path = filePath,
                content = string.IsNullOrWhiteSpace(content) ? null : content
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new
            {
                success = false,
                message = ex.Message
            });
        }
    }
}
