using AsaServerManager.Web.Contracts.Api.Admin;
using AsaServerManager.Web.Infrastructure.Auth;
using AsaServerManager.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace AsaServerManager.Web.Controllers;

[ApiController]
[RequireApiKey]
[Route("api/admin/game-config-ini")]
public sealed class GameConfigIniController(GameConfigService gameConfigService) : ControllerBase
{
    private readonly GameConfigService _gameConfigService = gameConfigService;

    [HttpPost("game-ini")]
    public Task<IActionResult> UploadGameIni([FromForm] UploadConfigFileRequest request, CancellationToken cancellationToken)
    {
        return UploadAsync(
            request.File,
            saveAsync: content => _gameConfigService.SaveGameIniAsync(content, cancellationToken),
            filePath: AsaServerManager.Web.Constants.GameConfigConstants.GameIniPath,
            successMessage: "Game.ini updated.",
            cancellationToken);
    }

    [HttpPost("game-user-settings-ini")]
    public Task<IActionResult> UploadGameUserSettingsIni([FromForm] UploadConfigFileRequest request, CancellationToken cancellationToken)
    {
        return UploadAsync(
            request.File,
            saveAsync: content => _gameConfigService.SaveGameUserSettingsIniAsync(content, cancellationToken),
            filePath: AsaServerManager.Web.Constants.GameConfigConstants.GameUserSettingsIniPath,
            successMessage: "GameUserSettings.ini updated.",
            cancellationToken);
    }

    private async Task<IActionResult> UploadAsync(
        IFormFile? file,
        Func<string, Task> saveAsync,
        string filePath,
        string successMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            string content = await ReadFileContentAsync(file, cancellationToken);
            await saveAsync(content);
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

        return Ok(new
        {
            success = true,
            message = successMessage,
            path = filePath
        });
    }

    private static async Task<string> ReadFileContentAsync(IFormFile? file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length <= 0)
        {
            throw new ArgumentException("A config file upload is required.");
        }

        using Stream stream = file.OpenReadStream();
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync(cancellationToken);
    }
}
