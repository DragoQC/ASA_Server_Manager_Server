using System.Net;
using System.Net.Sockets;
using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Infrastructure.Rcon;
using AsaServerManager.Web.Models.Rcon;

namespace AsaServerManager.Web.Services;

public sealed class RconService(ServerConfigService serverConfigService)
{
    private readonly ServerConfigService _serverConfigService = serverConfigService;

    public async Task<RconStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        ResolvedRconSettings resolved = await ResolveAsync(cancellationToken);
        return resolved.Status;
    }

    public async Task<RconProbeResult> ProbeAsync(CancellationToken cancellationToken = default)
    {
        ResolvedRconSettings resolved = await ResolveAsync(cancellationToken);
        if (!resolved.Status.CanExecute)
        {
            return new RconProbeResult(false, RconProtocolConstants.Host, resolved.Status.Port, resolved.Status.StateLabel, resolved.Status.Message);
        }

        try
        {
            await using RconConnection connection = await ConnectAndAuthenticateAsync(resolved.Status.Port, resolved.Password, cancellationToken);
            return new RconProbeResult(true, RconProtocolConstants.Host, resolved.Status.Port, "OK", "Connected.");
        }
        catch (Exception exception)
        {
            return new RconProbeResult(false, RconProtocolConstants.Host, resolved.Status.Port, "Unavailable", exception.Message);
        }
    }

    public async Task<string> ExecuteAsync(string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new InvalidOperationException("Command is required.");
        }

        ResolvedRconSettings resolved = await ResolveAsync(cancellationToken);
        if (!resolved.Status.CanExecute)
        {
            throw new InvalidOperationException(resolved.Status.Message);
        }

        await using RconConnection connection = await ConnectAndAuthenticateAsync(resolved.Status.Port, resolved.Password, cancellationToken);
        string response = await connection.ExecuteAsync(command.Trim(), cancellationToken);
        return string.IsNullOrWhiteSpace(response)
            ? "(No response)"
            : response.TrimEnd();
    }

    private async Task<ResolvedRconSettings> ResolveAsync(CancellationToken cancellationToken)
    {
        int fallbackPort = (await _serverConfigService.LoadAsync(cancellationToken)).RconPort;
        if (!File.Exists(GameConfigConstants.GameUserSettingsIniPath))
        {
            return new ResolvedRconSettings(
                new RconStatus(
                    false,
                    false,
                    false,
                    false,
                    false,
                    fallbackPort,
                    "Missing",
                    "GameUserSettings.ini missing."),
                string.Empty);
        }

        string content = await File.ReadAllTextAsync(GameConfigConstants.GameUserSettingsIniPath, cancellationToken);
        Dictionary<string, string> values = ParseServerSettings(content);
        int port = fallbackPort;

        if (values.TryGetValue("RCONPort", out string? configuredPort) &&
            int.TryParse(configuredPort, out int parsedPort) &&
            parsedPort is > 0 and <= 65535)
        {
            port = parsedPort;
        }

        bool hasEnabledKey = values.TryGetValue("RCONEnabled", out string? enabledValue);
        if (!hasEnabledKey)
        {
            return new ResolvedRconSettings(
                new RconStatus(true, false, false, false, false, port, "Missing", "RCONEnabled key missing."),
                string.Empty);
        }

        if (!bool.TryParse(enabledValue, out bool isEnabled))
        {
            return new ResolvedRconSettings(
                new RconStatus(true, true, false, false, false, port, "Invalid", "RCONEnabled is invalid."),
                string.Empty);
        }

        if (!isEnabled)
        {
            return new ResolvedRconSettings(
                new RconStatus(true, true, false, false, false, port, "Disabled", "RCON is disabled."),
                string.Empty);
        }

        bool hasPasswordKey = values.TryGetValue("RCONPassword", out string? password);
        if (!hasPasswordKey)
        {
            return new ResolvedRconSettings(
                new RconStatus(true, true, true, false, false, port, "Missing", "RCONPassword key missing."),
                string.Empty);
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            return new ResolvedRconSettings(
                new RconStatus(true, true, true, true, false, port, "Missing", "RCON password missing."),
                string.Empty);
        }

        return new ResolvedRconSettings(
            new RconStatus(true, true, true, true, true, port, "Enabled", "RCON is enabled."),
            password);
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
}
