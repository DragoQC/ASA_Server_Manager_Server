using System.Buffers;
using asa_server_node_api.Constants;
using asa_server_node_api.Models.GameConfig;

namespace asa_server_node_api.Services;

public sealed class GameConfigService
{
    private static readonly SearchValues<char> InvalidIniIdentifierCharacters = SearchValues.Create("/\\[]");

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

    public Task SaveGameIniAsync(string content, CancellationToken cancellationToken = default)
    {
        return SaveValidatedIniAsync(GameConfigConstants.GameIniPath, content, cancellationToken);
    }

    public Task SaveGameUserSettingsIniAsync(string content, CancellationToken cancellationToken = default)
    {
        return SaveValidatedIniAsync(GameConfigConstants.GameUserSettingsIniPath, content, cancellationToken);
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

    private async Task SaveValidatedIniAsync(string filePath, string content, CancellationToken cancellationToken)
    {
        EnsureCanEdit();
        ValidateIniContent(content);
        await File.WriteAllTextAsync(filePath, NormalizeContent(content), cancellationToken);
    }

    private static void ValidateIniContent(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Config file content is required.");
        }

        string[] lines = NormalizeContent(content).Split('\n');

        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmedLine) ||
                trimmedLine.StartsWith(';') ||
                trimmedLine.StartsWith('#'))
            {
                continue;
            }

            if (trimmedLine.AsSpan().IndexOfAny('\0', '\u001a') >= 0)
            {
                throw new ArgumentException("Config file contains invalid control characters.");
            }

            if (trimmedLine.StartsWith('[') && trimmedLine.EndsWith(']'))
            {
                string sectionName = trimmedLine[1..^1].Trim();
                if (string.IsNullOrWhiteSpace(sectionName))
                {
                    throw new ArgumentException("Config file contains an empty section name.");
                }

                if (sectionName.AsSpan().IndexOfAny(InvalidIniIdentifierCharacters) >= 0)
                {
                    throw new ArgumentException("Config file contains invalid section characters.");
                }

                continue;
            }

            int separatorIndex = trimmedLine.IndexOf('=');
            if (separatorIndex <= 0)
            {
                throw new ArgumentException("Config file contains a line without a valid key=value pair.");
            }

            string key = trimmedLine[..separatorIndex].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Config file contains an empty key.");
            }

            if (key.AsSpan().IndexOfAny(InvalidIniIdentifierCharacters) >= 0)
            {
                throw new ArgumentException("Config file contains invalid key characters.");
            }
        }
    }

    private static string NormalizeContent(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal);
    }
}
