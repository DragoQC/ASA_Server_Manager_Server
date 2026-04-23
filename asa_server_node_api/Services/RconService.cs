using System.Net;
using System.Net.Sockets;
using asa_server_node_api.Constants;
using asa_server_node_api.Infrastructure.Rcon;
using asa_server_node_api.Models.Rcon;

namespace asa_server_node_api.Services;

public sealed class RconService(ServerConfigService serverConfigService, GameConfigService gameConfigService)
{
    private readonly ServerConfigService _serverConfigService = serverConfigService;
    private readonly GameConfigService _gameConfigService = gameConfigService;

    public async Task<RconSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        RconContext context = await LoadContextAsync(cancellationToken);
        return context.Settings;
    }

    public async Task<RconStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        RconContext context = await LoadContextAsync(cancellationToken);
        return context.Status;
    }

    public async Task<RconProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        int fallbackPort = await _serverConfigService.GetRconPortAsync(cancellationToken);
        if (!_gameConfigService.HasGameUserSettingsIniFile())
        {
            return new RconProbeResult(false, RconProtocolConstants.Host, fallbackPort, "Missing", "GameUserSettings.ini missing.");
        }

        RconContext context = await LoadContextAsync(cancellationToken);
        if (!context.Status.CanExecute(context.Settings))
        {
            return new RconProbeResult(false, RconProtocolConstants.Host, context.Settings.Port, context.Status.StateLabel, context.Status.Message);
        }

        try
        {
            await using RconConnection connection = await ConnectAndAuthenticateAsync(context.Settings.Port, context.Settings.Password, cancellationToken);
            return new RconProbeResult(true, RconProtocolConstants.Host, context.Settings.Port, "Running", "Connected.");
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.ConnectionRefused)
        {
            return new RconProbeResult(false, RconProtocolConstants.Host, context.Settings.Port, "Waiting", "RCON is not reachable yet.");
        }
        catch (SocketException exception) when (exception.SocketErrorCode == SocketError.TimedOut)
        {
            return new RconProbeResult(false, RconProtocolConstants.Host, context.Settings.Port, "Waiting", "RCON timed out.");
        }
        catch (Exception exception)
        {
            return new RconProbeResult(false, RconProtocolConstants.Host, context.Settings.Port, "Unavailable", exception.Message);
        }
    }

    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        return await ExecuteCoreAsync(command, formatEmptyResponse: true, cancellationToken);
    }

    public async Task<int> GetOnlinePlayerCountAsync(CancellationToken cancellationToken = default)
    {
        string response = await ExecuteCoreAsync("ListPlayers", formatEmptyResponse: false, cancellationToken);
        return ParseOnlinePlayerCount(response);
    }

    private async Task<RconContext> LoadContextAsync(CancellationToken cancellationToken)
    {
        int fallbackPort = await _serverConfigService.GetRconPortAsync(cancellationToken);
        if (!_gameConfigService.HasGameUserSettingsIniFile())
        {
            throw new InvalidOperationException("GameUserSettings.ini missing.");
        }

        string content = await File.ReadAllTextAsync(GameConfigConstants.GameUserSettingsIniPath, cancellationToken);
        Dictionary<string, string> values = ParseServerSettings(content);
        RconSettings settings = BuildSettings(values, fallbackPort);
        RconStatus status = BuildStatus(values, settings);
        return new RconContext(settings, status);
    }

    private static Dictionary<string, string> ParseServerSettings(string content)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
        bool inServerSettings = false;

        foreach (string rawLine in content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
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

    private static RconSettings BuildSettings(IReadOnlyDictionary<string, string> values, int fallbackPort)
    {
        int port = fallbackPort;
        if (values.TryGetValue("RCONPort", out string? configuredPort) &&
            int.TryParse(configuredPort, out int parsedPort) &&
            parsedPort is > 0 and <= 65535)
        {
            port = parsedPort;
        }

        bool isEnabled = values.TryGetValue("RCONEnabled", out string? enabledValue) &&
                         bool.TryParse(enabledValue, out bool parsedEnabled) &&
                         parsedEnabled;

        string password = values.TryGetValue("ServerAdminPassword", out string? configuredPassword)
            ? configuredPassword ?? string.Empty
            : string.Empty;

        return new RconSettings(port, password, isEnabled);
    }

    private static RconStatus BuildStatus(IReadOnlyDictionary<string, string> values, RconSettings settings)
    {
        bool hasEnabledKey = values.TryGetValue("RCONEnabled", out string? enabledValue);
        if (!hasEnabledKey)
        {
            return new RconStatus(false, false, false, false, "Missing", "RCONEnabled key missing.");
        }

        if (!bool.TryParse(enabledValue, out _))
        {
            return new RconStatus(true, false, false, false, "Invalid", "RCONEnabled is invalid.");
        }

        bool hasPort = values.TryGetValue("RCONPort", out string? configuredPort) &&
                       int.TryParse(configuredPort, out int parsedPort) &&
                       parsedPort is > 0 and <= 65535;
        if (!settings.IsEnabled)
        {
            return new RconStatus(true, hasPort, false, false, "Disabled", "RCON is disabled.");
        }

        if (!hasPort)
        {
            return new RconStatus(true, false, false, false, "Missing", "RCONPort key missing or invalid.");
        }

        bool hasPasswordKey = values.TryGetValue("ServerAdminPassword", out _);
        if (!hasPasswordKey)
        {
            return new RconStatus(true, true, false, false, "Missing", "ServerAdminPassword key missing.");
        }

        bool hasPassword = !string.IsNullOrWhiteSpace(settings.Password);
        if (!hasPassword)
        {
            return new RconStatus(true, true, true, false, "Missing", "Server admin password missing.");
        }

        return new RconStatus(true, true, true, true, "Enabled", "RCON is enabled.");
    }

    private async Task<string> ExecuteCoreAsync(string command, bool formatEmptyResponse, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("Command is required.");
        }

        if (!_gameConfigService.HasGameUserSettingsIniFile())
        {
            throw new InvalidOperationException("GameUserSettings.ini missing.");
        }

        RconContext context = await LoadContextAsync(cancellationToken);
        if (!context.Status.CanExecute(context.Settings))
        {
            throw new InvalidOperationException(context.Status.Message);
        }

        await using RconConnection connection = await ConnectAndAuthenticateAsync(context.Settings.Port, context.Settings.Password, cancellationToken);
        string response = await connection.ExecuteAsync(command.Trim(), cancellationToken).ConfigureAwait(false);
        string trimmedResponse = response.TrimEnd();

        if (formatEmptyResponse && string.IsNullOrWhiteSpace(trimmedResponse))
        {
            return "Server did not respond. Please check the command syntax.";
        }

        return trimmedResponse;
    }

    private static int ParseOnlinePlayerCount(string response)
    {
        string[] lines = response
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length == 0)
        {
            return 0;
        }

        List<string> meaningfulLines = lines
            .Where(line => !IsNonPlayerLine(line))
            .ToList();

        if (meaningfulLines.Count == 0)
        {
            return 0;
        }

        int structuredPlayerLines = meaningfulLines.Count(IsStructuredPlayerLine);
        return structuredPlayerLines > 0
            ? structuredPlayerLines
            : meaningfulLines.Count;
    }

    private static bool IsNonPlayerLine(string line)
    {
        string normalized = line.Trim().ToUpperInvariant();

        return normalized.Length == 0 ||
               normalized.Contains("NO PLAYERS", StringComparison.Ordinal) ||
               normalized.Contains("NO ONE", StringComparison.Ordinal) ||
               normalized.StartsWith("CONNECTED PLAYERS", StringComparison.Ordinal) ||
               normalized.StartsWith("CURRENT PLAYERS", StringComparison.Ordinal) ||
               normalized.StartsWith("PLAYERS ONLINE", StringComparison.Ordinal) ||
               normalized.StartsWith("NAME,", StringComparison.Ordinal) ||
               normalized.StartsWith("INDEX,", StringComparison.Ordinal);
    }

    private static bool IsStructuredPlayerLine(string line)
    {
        string trimmed = line.Trim();

        int dotIndex = trimmed.IndexOf('.');
        if (dotIndex > 0 && int.TryParse(trimmed[..dotIndex], out _))
        {
            return true;
        }

        int spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex > 0 && int.TryParse(trimmed[..spaceIndex], out _))
        {
            return true;
        }

        return trimmed.Contains(',', StringComparison.Ordinal);
    }

    private static async Task<RconConnection> ConnectAndAuthenticateAsync(int port, string password, CancellationToken cancellationToken)
    {
        TcpClient tcpClient = new();
        tcpClient.NoDelay = true;

        using CancellationTokenSource timeoutCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCancellationTokenSource.CancelAfter(RconProtocolConstants.ProbeTimeoutMilliseconds);
        await tcpClient.ConnectAsync(IPAddress.Loopback, port, timeoutCancellationTokenSource.Token);

        NetworkStream stream = tcpClient.GetStream();
        RconConnection connection = new(tcpClient, stream);
        await connection.AuthenticateAsync(password, cancellationToken);
        return connection;
    }

    private sealed record RconContext(
        RconSettings Settings,
        RconStatus Status);
}
