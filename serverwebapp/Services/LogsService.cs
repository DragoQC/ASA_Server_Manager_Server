using AsaServerManager.Web.Constants;
using AsaServerManager.Web.Models.Asa;
using AsaServerManager.Web.Models.AsaLogs;

namespace AsaServerManager.Web.Services;

public sealed class LogsService(InstallStateService installStateService)
{
    private readonly InstallStateService _installStateService = installStateService;

    public async Task<AsaLogsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        AsaServiceStatus serviceStatus = await _installStateService.GetAsaServiceStatusAsync(cancellationToken);

        CommandResult statusResult = await RunCommandAsync(
            SystemCommandConstants.SudoPath,
            [SystemCommandConstants.SystemctlPath, "status", "asa", "--no-pager", "--full"],
            cancellationToken);

        CommandResult journalResult = await RunCommandAsync(
            SystemCommandConstants.SudoPath,
            [SystemCommandConstants.JournalctlPath, "-u", "asa", "-n", "80", "--no-pager"],
            cancellationToken);

        CommandResult wireGuardJournalResult = await RunCommandAsync(
            SystemCommandConstants.SudoPath,
            [SystemCommandConstants.JournalctlPath, "-u", InstallStateConstants.WireGuardServiceName, "-n", "80", "--no-pager"],
            cancellationToken);

        string statusContent = GetContentOrUnavailable(statusResult.Output);
        string wireGuardJournalContent = GetContentOrUnavailable(wireGuardJournalResult.Output);

        return new AsaLogsSnapshot(
            serviceStatus,
            new LogSectionSnapshot(
                "Service status",
                "Live systemctl status output for asa.service.",
                statusContent,
                !IsUnavailable(statusContent)),
            new LogSectionSnapshot(
                "WireGuard journal",
                "Recent journalctl output for wg-quick@wg0.",
                wireGuardJournalContent,
                !IsUnavailable(wireGuardJournalContent)),
            DateTimeOffset.UtcNow);
    }

    private static string GetContentOrUnavailable(string output)
    {
        return string.IsNullOrWhiteSpace(output)
            ? "Service unavailable or not present."
            : output.TrimEnd();
    }

    private static bool IsUnavailable(string value)
    {
        return string.Equals(value, "Service unavailable or not present.", StringComparison.Ordinal);
    }

    private static async Task<CommandResult> RunCommandAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        System.Diagnostics.Process process = new()
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        try
        {
            process.Start();
            Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            string standardOutput = await standardOutputTask;
            string standardError = await standardErrorTask;
            string combinedOutput = string.IsNullOrWhiteSpace(standardOutput)
                ? standardError
                : string.IsNullOrWhiteSpace(standardError)
                    ? standardOutput
                    : $"{standardOutput}\n{standardError}";

            return new CommandResult(process.ExitCode, combinedOutput);
        }
        finally
        {
            process.Dispose();
        }
    }
}
