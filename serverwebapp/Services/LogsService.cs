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

        CommandResult wireGuardJournalResult = await RunCommandAsync(
            SystemCommandConstants.SudoPath,
            [SystemCommandConstants.JournalctlPath, "-u", InstallStateConstants.WireGuardServiceName, "-n", "80", "--no-pager"],
            cancellationToken);

        CommandResult webAppJournalResult = await RunCommandAsync(
            SystemCommandConstants.SudoPath,
            [SystemCommandConstants.JournalctlPath, "-u", InstallStateConstants.WebAppServiceName, "-n", "80", "--no-pager"],
            cancellationToken);

        CommandResult nfsJournalResult = await RunCommandAsync(
            SystemCommandConstants.SudoPath,
            [SystemCommandConstants.JournalctlPath, "-u", InstallStateConstants.ClusterMountUnitName, "-n", "80", "--no-pager"],
            cancellationToken);

        string statusContent = GetContentOrUnavailable(statusResult.Output);
        string webAppJournalContent = GetWebAppContentOrUnavailable(webAppJournalResult.Output);
        string wireGuardJournalContent = GetContentOrUnavailable(wireGuardJournalResult.Output);
        string nfsJournalContent = GetContentOrUnavailable(nfsJournalResult.Output);

        return new AsaLogsSnapshot(
            serviceStatus,
            new LogSectionSnapshot(
                "Service status",
                "Live systemctl status output for asa.service.",
                statusContent,
                !IsUnavailable(statusContent)),
            new LogSectionSnapshot(
                "Web app journal",
                "Recent journalctl output for asa-webapp.",
                webAppJournalContent,
                !IsUnavailable(webAppJournalContent)),
            new LogSectionSnapshot(
                "WireGuard journal",
                "Recent journalctl output for wg-quick@wg0.",
                wireGuardJournalContent,
                !IsUnavailable(wireGuardJournalContent)),
            new LogSectionSnapshot(
                "NFS journal",
                "Recent journalctl output for opt-asa-cluster.mount.",
                nfsJournalContent,
                !IsUnavailable(nfsJournalContent)),
            DateTimeOffset.UtcNow);
    }

    private static string GetContentOrUnavailable(string output)
    {
        return string.IsNullOrWhiteSpace(output)
            ? "Service unavailable or not present."
            : output.TrimEnd();
    }

    private static string GetWebAppContentOrUnavailable(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "Service unavailable or not present.";
        }

        string[] filteredLines = output
            .Split('\n', StringSplitOptions.TrimEntries)
            .Where(line =>
                !line.Contains(" sudo[", StringComparison.Ordinal)
                && !line.Contains("pam_unix(sudo:session):", StringComparison.Ordinal))
            .ToArray();

        if (filteredLines.Length == 0)
        {
            return "No application log lines found after filtering sudo session noise.";
        }

        return string.Join('\n', filteredLines).TrimEnd();
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
