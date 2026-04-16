using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Models.GameConfig;

namespace AsaServerManager.Web.Services;

public sealed class GameConfigService
{
    public bool HasGameIniFile()
    {
        return File.Exists(GameConfigConstants.GameIniPath);
    }

    public bool HasGameUserSettingsIniFile()
    {
        return File.Exists(GameConfigConstants.GameUserSettingsIniPath);
    }

    public Task<IReadOnlyList<GameConfigFileState>> LoadStatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool hasConfigDirectory = Directory.Exists(GameConfigConstants.WindowsServerConfigRootPath);

        IReadOnlyList<GameConfigFileState> states =
        [
            GetGameIniFileState(hasConfigDirectory),
            GetGameUserSettingsIniFileState(hasConfigDirectory)
        ];

        return Task.FromResult(states);
    }

    public async Task<string> LoadEditorContentAsync(string filePath, CancellationToken cancellationToken = default)
    {
        EnsureCanEdit();

        if (!File.Exists(filePath))
        {
            return string.Empty;
        }

        return await File.ReadAllTextAsync(filePath, cancellationToken);
    }

    public async Task SaveAsync(string filePath, string content, CancellationToken cancellationToken = default)
    {
        EnsureCanEdit();
        await File.WriteAllTextAsync(filePath, NormalizeContent(content), cancellationToken);
    }

    private GameConfigFileState GetGameIniFileState(bool hasConfigDirectory)
    {
        bool exists = HasGameIniFile();
        string stateLabel = exists ? "OK" : "Missing";

        return new GameConfigFileState(
            "Game.ini",
            "Advanced server gameplay rules, overrides, and mod configuration values.",
            GameConfigConstants.GameIniPath,
            stateLabel,
            hasConfigDirectory,
            exists);
    }

    private GameConfigFileState GetGameUserSettingsIniFileState(bool hasConfigDirectory)
    {
        bool exists = HasGameUserSettingsIniFile();
        string stateLabel = exists ? "OK" : "Missing";

        return new GameConfigFileState(
            "GameUserSettings.ini",
            "Main server settings such as session options and gameplay tuning values.",
            GameConfigConstants.GameUserSettingsIniPath,
            stateLabel,
            hasConfigDirectory,
            exists);
    }

    private static void EnsureCanEdit()
    {
        if (!Directory.Exists(GameConfigConstants.WindowsServerConfigRootPath))
        {
            throw new InvalidOperationException("You must start the ARK server once before editing game config files.");
        }
    }

    private static string NormalizeContent(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
