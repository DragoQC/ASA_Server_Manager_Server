using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Models.GameConfig;

namespace AsaServerManager.Web.Services;

public sealed class GameConfigService
{
    public Task<IReadOnlyList<GameConfigFileState>> LoadStatesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        bool hasConfigDirectory = Directory.Exists(GameConfigConstants.WindowsServerConfigRootPath);

        IReadOnlyList<GameConfigFileState> states =
        [
            BuildFileState(
                title: "Game.ini",
                description: "Advanced server gameplay rules, overrides, and mod configuration values.",
                filePath: GameConfigConstants.GameIniPath,
                hasConfigDirectory),
            BuildFileState(
                title: "GameUserSettings.ini",
                description: "Main server settings such as session options and gameplay tuning values.",
                filePath: GameConfigConstants.GameUserSettingsIniPath,
                hasConfigDirectory)
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

    private static GameConfigFileState BuildFileState(
        string title,
        string description,
        string filePath,
        bool hasConfigDirectory)
    {
        bool exists = File.Exists(filePath);
        string stateLabel = exists ? "OK" : "Missing";

        return new GameConfigFileState(
            title,
            description,
            filePath,
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
