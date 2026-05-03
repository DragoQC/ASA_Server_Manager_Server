using System.Buffers;
using asa_server_node_api.Constants;
using asa_server_node_api.Models.GameConfig;

namespace asa_server_node_api.Services;

public sealed class GameConfigService
{
    private static readonly SearchValues<char> InvalidIniKeyCharacters = SearchValues.Create("/\\");
    private static readonly SearchValues<char> InvalidIniSectionCharacters = SearchValues.Create("[]");

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

    public async Task<IReadOnlyDictionary<string, string>> LoadGameUserServerSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!HasGameUserSettingsIniFile())
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        string content = await File.ReadAllTextAsync(GameConfigConstants.GameUserSettingsIniPath, cancellationToken);
        return ParseServerSettings(content);
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

                if (sectionName.AsSpan().IndexOfAny(InvalidIniSectionCharacters) >= 0)
                {
                    throw new ArgumentException("Config file contains invalid section characters. Unreal-style sections like [/Script/ShooterGame.ShooterGameMode] are allowed.");
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

            if (key.AsSpan().IndexOfAny(InvalidIniKeyCharacters) >= 0)
            {
                throw new ArgumentException("Config file contains invalid key characters.");
            }
        }
    }

    private static string NormalizeContent(string content)
    {
        return content.Replace("\r\n", "\n", StringComparison.Ordinal);
    }

    private static IReadOnlyDictionary<string, string> ParseServerSettings(string content)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        bool inServerSettings = false;

        foreach (string rawLine in NormalizeContent(content).Split('\n'))
        {
            string line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';') || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                inServerSettings = string.Equals(line, "[ServerSettings]", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (!inServerSettings)
            {
                continue;
            }

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();
            values[key] = Unquote(value);
        }

        return values;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
        {
            return value[1..^1];
        }

        return value;
    }
}
