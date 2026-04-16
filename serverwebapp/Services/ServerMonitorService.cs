using System.Diagnostics;
using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Models.Asa;
using AsaServerManager.Web.Models;

namespace AsaServerManager.Web.Services;

public sealed class ServerMonitorService(ILogger<ServerMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ILogger<ServerMonitorService> _logger = logger;
    private volatile AsaServerStatusSnapshot _current = AsaServerStatusSnapshot.Default(AsaServiceConstants.ServiceName);

    public event Action? StatusChanged;

    public AsaServerStatusSnapshot GetSnapshot() => _current;

    public Task RefreshNowAsync(CancellationToken cancellationToken = default) => RefreshAsync(cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        using PeriodicTimer timer = new(PollInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RefreshAsync(stoppingToken);
        }
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        AsaServerStatusSnapshot snapshot = await ProbeAsync(cancellationToken);
        _current = snapshot;
        StatusChanged?.Invoke();
    }

    private async Task<AsaServerStatusSnapshot> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using Process process = new()
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "systemctl",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.StartInfo.ArgumentList.Add("show");
            process.StartInfo.ArgumentList.Add(AsaServiceConstants.ServiceName);
            process.StartInfo.ArgumentList.Add("--property=LoadState");
            process.StartInfo.ArgumentList.Add("--property=ActiveState");
            process.StartInfo.ArgumentList.Add("--property=SubState");
            process.StartInfo.ArgumentList.Add("--property=UnitFileState");
            process.StartInfo.ArgumentList.Add("--property=MainPID");
            process.StartInfo.ArgumentList.Add("--property=Result");
            process.StartInfo.ArgumentList.Add("--property=ActiveEnterTimestamp");

            process.Start();

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            string stdout = await stdoutTask;
            string stderr = await stderrTask;

            if (process.ExitCode != 0)
            {
                if (stderr.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
                {
                    return new AsaServerStatusSnapshot(
                        AsaServiceConstants.ServiceName,
                        LoadState: "not-found",
                        ActiveState: "inactive",
                        SubState: "dead",
                        UnitFileState: "not-found",
                        MainPid: null,
                        Result: "missing",
                        ActiveSinceUtc: null,
                        CheckedAtUtc: DateTimeOffset.UtcNow,
                        ErrorMessage: null);
                }

                return new AsaServerStatusSnapshot(
                    AsaServiceConstants.ServiceName,
                    LoadState: "unknown",
                    ActiveState: "unknown",
                    SubState: "unknown",
                    UnitFileState: "unknown",
                    MainPid: null,
                    Result: "error",
                    ActiveSinceUtc: null,
                    CheckedAtUtc: DateTimeOffset.UtcNow,
                    ErrorMessage: string.IsNullOrWhiteSpace(stderr) ? "systemctl check failed." : stderr.Trim());
            }

            Dictionary<string, string> values = ParseProperties(stdout);

            return new AsaServerStatusSnapshot(
                AsaServiceConstants.ServiceName,
                LoadState: ReadValue(values, "LoadState", "unknown"),
                ActiveState: ReadValue(values, "ActiveState", "unknown"),
                SubState: ReadValue(values, "SubState", "unknown"),
                UnitFileState: ReadValue(values, "UnitFileState", "unknown"),
                MainPid: ParsePid(ReadValue(values, "MainPID", "0")),
                Result: ReadValue(values, "Result", "unknown"),
                ActiveSinceUtc: AsaServiceStatusFactory.TryParseSystemdTimestamp(ReadValue(values, "ActiveEnterTimestamp", string.Empty)),
                CheckedAtUtc: DateTimeOffset.UtcNow,
                ErrorMessage: null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh ASA service state.");

            return new AsaServerStatusSnapshot(
                AsaServiceConstants.ServiceName,
                LoadState: "unknown",
                ActiveState: "unknown",
                SubState: "unknown",
                UnitFileState: "unknown",
                MainPid: null,
                Result: "error",
                ActiveSinceUtc: null,
                CheckedAtUtc: DateTimeOffset.UtcNow,
                ErrorMessage: ex.Message);
        }
    }

    private static Dictionary<string, string> ParseProperties(string output)
    {
        Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            string key = line[..separatorIndex];
            string value = line[(separatorIndex + 1)..];
            values[key] = value;
        }

        return values;
    }

    private static string ReadValue(IReadOnlyDictionary<string, string> values, string key, string fallback) =>
        values.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static int? ParsePid(string value) =>
        int.TryParse(value, out int pid) && pid > 0 ? pid : null;
}
