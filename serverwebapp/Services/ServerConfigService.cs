using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Models.ServerConfig;

namespace AsaServerManager.Web.Services;

public sealed class ServerConfigService
{
    private readonly SemaphoreSlim _sync = new(1, 1);
    private ServerConfigSettings? _cachedSettings;
    private bool _hasConfigFile;
    private DateTimeOffset _lastLoadedWriteTimeUtc;

	public async Task<ServerConfigSettings> LoadAsync(CancellationToken cancellationToken = default)
	{
        await _sync.WaitAsync(cancellationToken);
        try
        {
            DateTimeOffset currentWriteTimeUtc = File.Exists(ServerConfigConstants.EnvFilePath)
                ? File.GetLastWriteTimeUtc(ServerConfigConstants.EnvFilePath)
                : DateTimeOffset.MinValue;

            bool needsReload = _cachedSettings is null ||
                               _hasConfigFile != File.Exists(ServerConfigConstants.EnvFilePath) ||
                               (_hasConfigFile && currentWriteTimeUtc != _lastLoadedWriteTimeUtc);

            if (needsReload)
            {
                await ReloadCacheAsync(cancellationToken);
            }

            return (_cachedSettings ?? ServerConfigSettings.Default()).Clone();
        }
        finally
        {
            _sync.Release();
        }
	}

	public bool HasConfigFile()
	{
        return _cachedSettings is not null
            ? _hasConfigFile
            : File.Exists(ServerConfigConstants.EnvFilePath);
	}

	public async Task SaveAsync(ServerConfigSettings settings, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(settings);

        await _sync.WaitAsync(cancellationToken);
        try
        {
            await SaveInternalAsync(settings.Clone(), cancellationToken);
        }
        finally
        {
            _sync.Release();
        }
	}

	public async Task<List<string>> LoadModIdsAsync(CancellationToken cancellationToken = default)
	{
		ServerConfigSettings settings = await LoadAsync(cancellationToken);
		return settings.ModIds
				.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Where(value => !string.IsNullOrWhiteSpace(value))
				.ToList();
	}

    public async Task<int> GetMaxPlayersAsync(CancellationToken cancellationToken = default)
    {
        ServerConfigSettings settings = await LoadAsync(cancellationToken);
        return settings.MaxPlayers;
    }

    public async Task<int> GetRconPortAsync(CancellationToken cancellationToken = default)
    {
        ServerConfigSettings settings = await LoadAsync(cancellationToken);
        return settings.RconPort;
    }

    private async Task ReloadCacheAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(ServerConfigConstants.EnvFilePath))
        {
            _cachedSettings = ServerConfigSettings.Default();
            _hasConfigFile = false;
            _lastLoadedWriteTimeUtc = DateTimeOffset.MinValue;
            return;
        }

        string content = await File.ReadAllTextAsync(ServerConfigConstants.EnvFilePath, cancellationToken);
        ServerConfigSettings settings = GetSettings(content);
        string normalizedContent = NormalizeLineEndings(content);
        string expectedContent = NormalizeLineEndings(BuildEnvContent(settings));

        if (!string.Equals(normalizedContent, expectedContent, StringComparison.Ordinal))
        {
            await SaveInternalAsync(settings.Clone(), cancellationToken);
            return;
        }

        _cachedSettings = settings;
        _hasConfigFile = true;
        _lastLoadedWriteTimeUtc = File.GetLastWriteTimeUtc(ServerConfigConstants.EnvFilePath);
    }

    private async Task SaveInternalAsync(ServerConfigSettings settings, CancellationToken cancellationToken)
    {
        string? directoryPath = Path.GetDirectoryName(ServerConfigConstants.EnvFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string content = BuildEnvContent(settings);
        await File.WriteAllTextAsync(ServerConfigConstants.EnvFilePath, content, cancellationToken);

        _cachedSettings = settings.Clone();
        _hasConfigFile = true;
        _lastLoadedWriteTimeUtc = File.GetLastWriteTimeUtc(ServerConfigConstants.EnvFilePath);
    }

	private static ServerConfigSettings GetSettings(string? content)
	{
		Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
		string[] lines = NormalizeLineEndings(content ?? string.Empty).Split('\n');

		foreach (string line in lines)
		{
			string trimmedLine = line.Trim();
			if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
			{
				continue;
			}

			int separatorIndex = trimmedLine.IndexOf('=');
			if (separatorIndex <= 0)
			{
				continue;
			}

			string key = trimmedLine[..separatorIndex].Trim();
			string rawValue = trimmedLine[(separatorIndex + 1)..].Trim();
			values[key] = Unquote(rawValue);
		}

		ServerConfigSettings settings = ServerConfigSettings.Default();

		if (values.TryGetValue("MAP_NAME", out string? mapName) && !string.IsNullOrWhiteSpace(mapName))
		{
			settings.MapName = mapName;
		}

		if (values.TryGetValue("SERVER_NAME", out string? serverName) && !string.IsNullOrWhiteSpace(serverName))
		{
			settings.ServerName = serverName;
		}

		if (values.TryGetValue("MAX_PLAYERS", out string? maxPlayers) && int.TryParse(maxPlayers, out int maxPlayersValue))
		{
			settings.MaxPlayers = maxPlayersValue;
		}

		if (values.TryGetValue("GAME_PORT", out string? gamePort) && int.TryParse(gamePort, out int gamePortValue))
		{
			settings.GamePort = gamePortValue;
		}

		if (values.TryGetValue("QUERY_PORT", out string? queryPort) && int.TryParse(queryPort, out int queryPortValue))
		{
			settings.QueryPort = queryPortValue;
		}

		if (values.TryGetValue("RCON_PORT", out string? rconPort) && int.TryParse(rconPort, out int rconPortValue))
		{
			settings.RconPort = rconPortValue;
		}

		if (values.TryGetValue("MOD_IDS", out string? modIds))
		{
			settings.ModIds = modIds ?? string.Empty;
		}

		if (values.TryGetValue("CLUSTER_ID", out string? clusterId))
		{
			settings.ClusterId = clusterId ?? string.Empty;
		}

		if (values.TryGetValue("CLUSTER_DIR", out string? clusterDir) && !string.IsNullOrWhiteSpace(clusterDir))
		{
			settings.ClusterDir = clusterDir;
		}

		if (values.TryGetValue("EXTRA_ARGS", out string? configuredExtraArgs))
		{
			settings.CustomExtraArgs = configuredExtraArgs ?? string.Empty;
		}

		return settings;
	}

	private static string BuildEnvContent(ServerConfigSettings settings)
	{
		return $"""
        MAP_NAME="{Escape(settings.MapName)}"
        SERVER_NAME="{Escape(settings.ServerName)}"
        MAX_PLAYERS={settings.MaxPlayers}
        GAME_PORT={settings.GamePort}
        QUERY_PORT={settings.QueryPort}
        RCON_PORT={settings.RconPort}
        MOD_IDS="{Escape(settings.ModIds)}"
        CLUSTER_ID="{Escape(settings.ClusterId)}"
        CLUSTER_DIR="{Escape(settings.ClusterDir)}"
        EXTRA_ARGS="{Escape(settings.CustomExtraArgs)}"
        """;
	}

	private static string NormalizeLineEndings(string value)
	{
		return value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
	}

	private static string Unquote(string value)
	{
		if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
		{
			return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
		}

		return value;
	}

	private static string Escape(string value)
	{
		return value.Replace("\"", "\\\"", StringComparison.Ordinal);
	}
}
