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

    public async Task SaveRawAsync(string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Config file content is required.");
		}

		await _sync.WaitAsync(cancellationToken);
        try
        {
            ValidateRawContent(content);

            ServerConfigSettings settings = ParseEnvContent(content);
            if (string.IsNullOrWhiteSpace(settings.ClusterDir))
            {
                settings.ClusterDir = ServerConfigSettings.Default().ClusterDir;
            }

            string normalizedContent = BuildEnvContent(settings);
            string? directoryPath = Path.GetDirectoryName(ServerConfigConstants.EnvFilePath);
            if (!string.IsNullOrWhiteSpace(directoryPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            await File.WriteAllTextAsync(ServerConfigConstants.EnvFilePath, normalizedContent, cancellationToken);

            _cachedSettings = settings.Clone();
            _hasConfigFile = true;
            _lastLoadedWriteTimeUtc = File.GetLastWriteTimeUtc(ServerConfigConstants.EnvFilePath);
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

    public async Task<string> UpdateClusterIdAsync(string? clusterId, CancellationToken cancellationToken = default)
    {
        string normalizedClusterId = NormalizeClusterId(clusterId);
        ServerConfigSettings settings = await LoadAsync(cancellationToken);
        settings.ClusterId = normalizedClusterId;
        await SaveAsync(settings, cancellationToken);
        return normalizedClusterId;
    }

    public async Task<string> UpdateClusterDirAsync(string? clusterDir, CancellationToken cancellationToken = default)
    {
        string normalizedClusterDir = NormalizeClusterDir(clusterDir);
        ServerConfigSettings settings = await LoadAsync(cancellationToken);
        settings.ClusterDir = normalizedClusterDir;
        await SaveAsync(settings, cancellationToken);
        return normalizedClusterDir;
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
        ServerConfigSettings settings = ParseEnvContent(content);
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

        if (string.IsNullOrWhiteSpace(settings.ClusterDir))
        {
            settings.ClusterDir = ServerConfigSettings.Default().ClusterDir;
        }

        string content = BuildEnvContent(settings);
        await File.WriteAllTextAsync(ServerConfigConstants.EnvFilePath, content, cancellationToken);

        _cachedSettings = settings.Clone();
        _hasConfigFile = true;
        _lastLoadedWriteTimeUtc = File.GetLastWriteTimeUtc(ServerConfigConstants.EnvFilePath);
    }

	private static ServerConfigSettings ParseEnvContent(string? content)
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

		if (values.TryGetValue(ServerConfigConstants.MapNameEnvKey, out string? mapName) && !string.IsNullOrWhiteSpace(mapName))
		{
			settings.MapName = mapName;
		}

		if (values.TryGetValue(ServerConfigConstants.ServerNameEnvKey, out string? serverName) && !string.IsNullOrWhiteSpace(serverName))
		{
			settings.ServerName = serverName;
		}

		if (values.TryGetValue(ServerConfigConstants.MaxPlayersEnvKey, out string? maxPlayers) && int.TryParse(maxPlayers, out int maxPlayersValue))
		{
			settings.MaxPlayers = maxPlayersValue;
		}

		if (values.TryGetValue(ServerConfigConstants.GamePortEnvKey, out string? gamePort) && int.TryParse(gamePort, out int gamePortValue))
		{
			settings.GamePort = gamePortValue;
		}

		if (values.TryGetValue(ServerConfigConstants.QueryPortEnvKey, out string? queryPort) && int.TryParse(queryPort, out int queryPortValue))
		{
			settings.QueryPort = queryPortValue;
		}

		if (values.TryGetValue(ServerConfigConstants.RconPortEnvKey, out string? rconPort) && int.TryParse(rconPort, out int rconPortValue))
		{
			settings.RconPort = rconPortValue;
		}

		if (values.TryGetValue(ServerConfigConstants.ModIdsEnvKey, out string? modIds))
		{
			settings.ModIds = modIds ?? string.Empty;
		}

		if (values.TryGetValue(ServerConfigConstants.ClusterIdEnvKey, out string? clusterId))
		{
			settings.ClusterId = clusterId ?? string.Empty;
		}

		if (values.TryGetValue(ServerConfigConstants.ExtraArgsEnvKey, out string? configuredExtraArgs))
		{
			settings.CustomExtraArgs = configuredExtraArgs ?? string.Empty;
		}

		if (values.TryGetValue(ServerConfigConstants.ClusterDirEnvKey, out string? clusterDir) && !string.IsNullOrWhiteSpace(clusterDir))
		{
			settings.ClusterDir = clusterDir;
		}

		return settings;
	}

	private static string BuildEnvContent(ServerConfigSettings settings)
	{
        List<string> lines =
        [
            $"{ServerConfigConstants.MapNameEnvKey}=\"{Escape(settings.MapName)}\"",
            $"{ServerConfigConstants.ServerNameEnvKey}=\"{Escape(settings.ServerName)}\"",
            $"{ServerConfigConstants.MaxPlayersEnvKey}={settings.MaxPlayers}",
            $"{ServerConfigConstants.GamePortEnvKey}={settings.GamePort}",
            $"{ServerConfigConstants.QueryPortEnvKey}={settings.QueryPort}",
            $"{ServerConfigConstants.RconPortEnvKey}={settings.RconPort}",
            $"{ServerConfigConstants.ModIdsEnvKey}=\"{Escape(settings.ModIds)}\"",
            $"{ServerConfigConstants.ClusterIdEnvKey}=\"{Escape(settings.ClusterId)}\"",
            $"{ServerConfigConstants.ClusterDirEnvKey}=\"{Escape(settings.ClusterDir)}\"",
            $"{ServerConfigConstants.ExtraArgsEnvKey}=\"{Escape(settings.CustomExtraArgs)}\""
        ];
        return string.Join('\n', lines) + "\n";
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

    private static string NormalizeClusterId(string? clusterId)
    {
        string normalizedClusterId = clusterId?.Trim() ?? string.Empty;

        if (normalizedClusterId.IndexOfAny(['\0', '\r', '\n', '\u001a']) >= 0)
        {
            throw new ArgumentException("Cluster ID contains invalid characters.");
        }

        if (normalizedClusterId.Length > 128)
        {
            throw new ArgumentException("Cluster ID cannot exceed 128 characters.");
        }

        return normalizedClusterId;
    }

    private static string NormalizeClusterDir(string? clusterDir)
    {
        string normalizedClusterDir = clusterDir?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(normalizedClusterDir))
        {
            throw new ArgumentException("Cluster dir is required.");
        }

        if (normalizedClusterDir.IndexOfAny(['\0', '\r', '\n', '\u001a']) >= 0)
        {
            throw new ArgumentException("Cluster dir contains invalid characters.");
        }

        if (normalizedClusterDir.Length > 256)
        {
            throw new ArgumentException("Cluster dir cannot exceed 256 characters.");
        }

        return normalizedClusterDir;
    }

	private static void ValidateRawContent(string content)
	{
		string[] lines = NormalizeLineEndings(content).Split('\n');

		foreach (string line in lines)
		{
			string trimmedLine = line.Trim();
			if (string.IsNullOrWhiteSpace(trimmedLine) || trimmedLine.StartsWith('#'))
			{
				continue;
			}

			if (trimmedLine.AsSpan().IndexOfAny('\0', '\u001a') >= 0)
			{
				throw new ArgumentException("Env file contains invalid control characters.");
			}

			int separatorIndex = trimmedLine.IndexOf('=');
			if (separatorIndex <= 0)
			{
				throw new ArgumentException("Env file contains a line without a valid KEY=value pair.");
			}

			string key = trimmedLine[..separatorIndex].Trim();
			if (string.IsNullOrWhiteSpace(key))
			{
				throw new ArgumentException("Env file contains an empty key.");
			}

			if (!ServerConfigConstants.EnvKeys.Contains(key))
			{
				throw new ArgumentException($"Env file contains an unsupported key: {key}.");
			}
		}
	}
}
