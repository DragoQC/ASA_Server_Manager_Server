using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Contracts.Api.Admin;

namespace AsaServerManager.Web.Services;

public sealed class UploadedFileService(
    ServerConfigService serverConfigService,
    GameConfigService gameConfigService)
{
    private readonly ServerConfigService _serverConfigService = serverConfigService;
    private readonly GameConfigService _gameConfigService = gameConfigService;

    public async Task<UploadFileResult> UploadServerConfigAsync(IFormFile? file, CancellationToken cancellationToken = default)
    {
        try
        {
            string content = await ReadContentAsync(file);
            await _serverConfigService.SaveRawAsync(content, cancellationToken);

            return new UploadFileResult(true, "asa.env updated.", ServerConfigConstants.EnvFilePath);
        }
        catch (ArgumentException ex)
        {
            return new UploadFileResult(false, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            return new UploadFileResult(false, ex.Message, null);
        }
    }

    public async Task<UploadFileResult> UploadGameIniAsync(IFormFile? file, CancellationToken cancellationToken = default)
    {
        try
        {
            string content = await ReadContentAsync(file);
            await _gameConfigService.SaveGameIniAsync(content, cancellationToken);

            return new UploadFileResult(true, "Game.ini updated.", GameConfigConstants.GameIniPath);
        }
        catch (ArgumentException ex)
        {
            return new UploadFileResult(false, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            return new UploadFileResult(false, ex.Message, null);
        }
    }

    public async Task<UploadFileResult> UploadGameUserSettingsIniAsync(IFormFile? file, CancellationToken cancellationToken = default)
    {
        try
        {
            string content = await ReadContentAsync(file);
            await _gameConfigService.SaveGameUserSettingsIniAsync(content, cancellationToken);

            return new UploadFileResult(true, "GameUserSettings.ini updated.", GameConfigConstants.GameUserSettingsIniPath);
        }
        catch (ArgumentException ex)
        {
            return new UploadFileResult(false, ex.Message, null);
        }
        catch (InvalidOperationException ex)
        {
            return new UploadFileResult(false, ex.Message, null);
        }
    }

    private static async Task<string> ReadContentAsync(IFormFile? file)
    {
        if (file is null || file.Length <= 0)
        {
            throw new ArgumentException("A config file upload is required.");
        }

        using Stream stream = file.OpenReadStream();
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
    }
}
