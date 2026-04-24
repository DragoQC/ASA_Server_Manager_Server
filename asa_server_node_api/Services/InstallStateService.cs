using asa_server_node_api.Constants;
using asa_server_node_api.Contracts.Api.Admin;
using asa_server_node_api.Models.Asa;
using asa_server_node_api.Models.Install;
using asa_server_node_api.Models.ServerConfig;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace asa_server_node_api.Services;

public sealed class InstallStateService(
    IWebHostEnvironment environment,
    ILogger<InstallStateService> logger,
    ProtonInstallService protonInstallService,
    ServerConfigService serverConfigService,
    AdminInstallStateHubService adminInstallStateHubService)
{
    private readonly IWebHostEnvironment _environment = environment;
    private readonly ILogger<InstallStateService> _logger = logger;
    private readonly ProtonInstallService _protonInstallService = protonInstallService;
    private readonly ServerConfigService _serverConfigService = serverConfigService;
    private readonly AdminInstallStateHubService _adminInstallStateHubService = adminInstallStateHubService;

    public async Task<InstallWorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            InstallToolState proton = await _protonInstallService.LoadStateAsync(cancellationToken);
            InstallToolState steam = BuildSteamState();
            InstallFileState startScript = await LoadFileStateAsync(
                "Start script",
                "Bootstraps the ASA dedicated server through Proton.",
                InstallStateConstants.StartScriptPath,
                "start-asa.sh",
                cancellationToken);

            InstallFileState serviceFile = await LoadFileStateAsync(
                "asa.service",
                "Registers the server with systemd so it can start with the machine and restart on failure.",
                InstallStateConstants.ServiceFilePath,
                "asa.service",
                cancellationToken);

            return new InstallWorkspaceSnapshot(
                proton,
                steam,
                startScript,
                serviceFile,
                DateTimeOffset.UtcNow);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load install workspace.");
            throw;
        }
    }

    public Task<InstallToolState> LoadSteamStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(BuildSteamState());
    }

    public Task<ServerConfigSettings> LoadExistingServerConfigAsync(CancellationToken cancellationToken = default)
    {
        return _serverConfigService.LoadExistingAsync(cancellationToken);
    }

    public async Task<ServerConfigSettings> PatchServerConfigAsync(PatchServerConfigRequest request, CancellationToken cancellationToken = default)
    {
        ServerConfigSettings settings = await _serverConfigService.PatchAsync(request, cancellationToken);
        await RestartAsaAfterConfigChangeAsync(cancellationToken);
        return settings;
    }

    public async Task<InstallAllResponse> InstallAllAsync(CancellationToken cancellationToken = default)
    {
        await _adminInstallStateHubService.BroadcastProgressAsync(
            new InstallProgressSnapshot(
                Operation: "install-all",
                Step: "received",
                State: "Running",
                Message: "Install all command received.",
                UpdatedAtUtc: DateTimeOffset.UtcNow),
            cancellationToken);

        try
        {
            InstallWorkspaceSnapshot snapshot = await LoadAsync(cancellationToken);

            string protonMessage = await _protonInstallService.UpdateAsync(null, cancellationToken);
            await _adminInstallStateHubService.BroadcastProgressAsync(
                new InstallProgressSnapshot("install-all", "proton", "Completed", protonMessage, DateTimeOffset.UtcNow),
                cancellationToken);
            await _adminInstallStateHubService.BroadcastWorkspaceAsync(cancellationToken);

            string steamMessage = await InstallSteamAsync(cancellationToken);
            await _adminInstallStateHubService.BroadcastProgressAsync(
                new InstallProgressSnapshot("install-all", "steamcmd", "Completed", steamMessage, DateTimeOffset.UtcNow),
                cancellationToken);
            await _adminInstallStateHubService.BroadcastWorkspaceAsync(cancellationToken);

            await SaveStartScriptAsync(snapshot.StartScript.Content, cancellationToken);
            await _adminInstallStateHubService.BroadcastProgressAsync(
                new InstallProgressSnapshot("install-all", "start-script", "Completed", "Start script applied.", DateTimeOffset.UtcNow),
                cancellationToken);
            await _adminInstallStateHubService.BroadcastWorkspaceAsync(cancellationToken);

            await SaveServiceFileAsync(snapshot.ServiceFile.Content, cancellationToken);
            await _adminInstallStateHubService.BroadcastProgressAsync(
                new InstallProgressSnapshot("install-all", "service-file", "Completed", "Service file applied.", DateTimeOffset.UtcNow),
                cancellationToken);
            await _adminInstallStateHubService.BroadcastWorkspaceAsync(cancellationToken);

            ServerConfigSettings serverConfig = await _serverConfigService.EnsureExistsAsync(cancellationToken);
            await _adminInstallStateHubService.BroadcastProgressAsync(
                new InstallProgressSnapshot("install-all", "server-config", "Completed", "Default server config ensured.", DateTimeOffset.UtcNow),
                cancellationToken);
            await _adminInstallStateHubService.BroadcastWorkspaceAsync(cancellationToken);

            await _adminInstallStateHubService.BroadcastProgressAsync(
                new InstallProgressSnapshot("install-all", "completed", "Completed", "Install all finished.", DateTimeOffset.UtcNow),
                cancellationToken);

            return new InstallAllResponse(
                ProtonMessage: protonMessage,
                SteamMessage: steamMessage,
                StartScriptMessage: "Start script applied.",
                ServiceFileMessage: "Service file applied.",
                ServerConfig: serverConfig);
        }
        catch (Exception ex)
        {
            await _adminInstallStateHubService.BroadcastProgressAsync(
                new InstallProgressSnapshot("install-all", "failed", "Failed", ex.Message, DateTimeOffset.UtcNow),
                cancellationToken);
            throw;
        }
    }

    public async Task<string> InstallSteamAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(InstallStateConstants.SteamRootPath);

        if (File.Exists(InstallStateConstants.SteamCmdPath))
        {
            foreach (string directoryPath in Directory.GetDirectories(InstallStateConstants.SteamRootPath, "*", SearchOption.TopDirectoryOnly))
            {
                Directory.Delete(directoryPath, recursive: true);
            }

            foreach (string filePath in Directory.GetFiles(InstallStateConstants.SteamRootPath, "*", SearchOption.TopDirectoryOnly))
            {
                File.Delete(filePath);
            }
        }

        await RunProcessAsync(
            "/usr/bin/env",
            ["wget", "-q", "-O", InstallStateConstants.SteamCmdArchivePath, InstallStateConstants.SteamCmdDownloadUrl],
            cancellationToken);

        await RunProcessAsync(
            "/usr/bin/env",
            ["tar", "-xzf", InstallStateConstants.SteamCmdArchivePath, "-C", InstallStateConstants.SteamRootPath],
            cancellationToken);

        if (File.Exists(InstallStateConstants.SteamCmdArchivePath))
        {
            File.Delete(InstallStateConstants.SteamCmdArchivePath);
        }

        if (!File.Exists(InstallStateConstants.SteamCmdPath))
        {
            throw new InvalidOperationException("SteamCMD install did not create /opt/asa/steam/steamcmd.sh.");
        }

        return "Installed SteamCMD.";
    }

    public async Task SaveStartScriptAsync(string content, CancellationToken cancellationToken = default)
    {
        await SaveFileAsync(InstallStateConstants.StartScriptPath, content, makeExecutable: true, cancellationToken);
    }

    public async Task SaveServiceFileAsync(string content, CancellationToken cancellationToken = default)
    {
        await SaveFileAsync(InstallStateConstants.ServiceFilePath, content, makeExecutable: false, cancellationToken);
    }

    public async Task<string> EnableAsaServiceAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", SystemCommandConstants.SystemctlPath, "daemon-reload"],
            cancellationToken);

        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", SystemCommandConstants.SystemctlPath, "enable", "asa"],
            cancellationToken);

        return "Reloaded systemd and enabled asa.";
    }

    public async Task<string> StartAsaServiceAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> missingItems = GetMissingValidationItems();
        if (missingItems.Count > 0)
        {
            throw new InvalidOperationException($"asa cannot be started. Missing: {string.Join(", ", missingItems)}.");
        }

        AsaServiceStatus status = await GetAsaServiceStatusAsync(cancellationToken);
        if (!status.CanStart)
        {
            throw new InvalidOperationException(status.ActiveState == "activating"
                ? "asa is already starting."
                : status.ActiveState == "active"
                    ? "asa is already running."
                    : $"asa cannot be started while it is {status.DisplayText.ToLowerInvariant()}.");
        }

        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", SystemCommandConstants.SystemctlPath, "start", "--no-block", "asa"],
            cancellationToken);

        return "Start command accepted for asa.";
    }

    public async Task<string> StopAsaServiceAsync(CancellationToken cancellationToken = default)
    {
        AsaServiceStatus status = await GetAsaServiceStatusAsync(cancellationToken);
        if (!status.CanStop)
        {
            throw new InvalidOperationException(status.ActiveState == "inactive" || status.ActiveState == "failed"
                ? "asa is already stopped."
                : $"asa cannot be stopped while it is {status.DisplayText.ToLowerInvariant()}.");
        }

        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", SystemCommandConstants.SystemctlPath, "stop", "--no-block", "asa"],
            cancellationToken);

        return "Stop command accepted for asa.";
    }

    public async Task<string> RestartAsaServiceAsync(CancellationToken cancellationToken = default)
    {
        AsaServiceStatus status = await GetAsaServiceStatusAsync(cancellationToken);
        if (!string.Equals(status.ActiveState, "active", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"asa is not running. Current state: {status.DisplayText}.");
        }

        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", SystemCommandConstants.SystemctlPath, "restart", "--no-block", "asa"],
            cancellationToken);

        return "Restart command accepted for asa.";
    }

    public async Task<string> RestartAsaAfterConfigChangeAsync(CancellationToken cancellationToken = default)
    {
        AsaServiceStatus status = await GetAsaServiceStatusAsync(cancellationToken);
        if (status.IsUnavailable)
        {
            throw new InvalidOperationException("asa service state is unavailable.");
        }

        if (status.IsRunning || status.IsStarting)
        {
            await RunProcessAsync(
                SystemCommandConstants.SudoPath,
                ["-n", SystemCommandConstants.SystemctlPath, "restart", "--no-block", "asa"],
                cancellationToken);

            return "Restart command accepted for asa.";
        }

        return $"asa config updated. Current state: {status.DisplayText}. No restart was performed.";
    }

    public async Task<string> RestartAsaIfRunningAsync(CancellationToken cancellationToken = default)
    {
        AsaServiceStatus status = await GetAsaServiceStatusAsync(cancellationToken);
        if (!string.Equals(status.ActiveState, "active", StringComparison.Ordinal))
        {
            return $"asa is not running. Current state: {status.DisplayText}. No restart was performed.";
        }

        return await RestartAsaServiceAsync(cancellationToken);
    }

    public async Task<string> EnableWireGuardAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", SystemCommandConstants.SystemctlPath, "enable", InstallStateConstants.WireGuardServiceName],
            cancellationToken);

        return $"Enabled {InstallStateConstants.WireGuardServiceName}.";
    }

    public async Task<string> StartWireGuardAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", SystemCommandConstants.SystemctlPath, "start", InstallStateConstants.WireGuardServiceName],
            cancellationToken);

        return $"Started {InstallStateConstants.WireGuardServiceName}.";
    }

    public async Task<string> RestartWireGuardAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", SystemCommandConstants.SystemctlPath, "restart", InstallStateConstants.WireGuardServiceName],
            cancellationToken);

        return $"Restarted {InstallStateConstants.WireGuardServiceName}.";
    }

    public async Task<string> EnableAndRestartWireGuardAsync(CancellationToken cancellationToken = default)
    {
        await EnableWireGuardAsync(cancellationToken);
        return await RestartWireGuardAsync(cancellationToken);
    }

    public async Task<string> ApplyNfsClientConfigAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", InstallStateConstants.ApplyNfsClientConfigScriptPath],
            cancellationToken);

        return "Updated /etc/fstab from the saved NFS client config.";
    }

    public async Task<string> StopWireGuardAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", SystemCommandConstants.SystemctlPath, "stop", InstallStateConstants.WireGuardServiceName],
            cancellationToken);

        return $"Stopped {InstallStateConstants.WireGuardServiceName}.";
    }

    public async Task<AsaServiceStatus> GetAsaServiceStatusAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> missingItems = GetMissingValidationItems();

        try
        {
            string output = await RunProcessForOutputAsync(
                SystemCommandConstants.SudoPath,
                ["-n", SystemCommandConstants.SystemctlPath, "show", "asa", "--property=ActiveState", "--property=SubState", "--property=Result", "--property=UnitFileState", "--property=ActiveEnterTimestamp"],
                cancellationToken);

            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            string[] lines = output.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                int separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                string key = line[..separatorIndex].Trim();
                string value = line[(separatorIndex + 1)..].Trim();
                values[key] = value;
            }

            string activeState = values.TryGetValue("ActiveState", out string? resolvedActiveState) && !string.IsNullOrWhiteSpace(resolvedActiveState)
                ? resolvedActiveState
                : "unknown";
            string subState = values.TryGetValue("SubState", out string? resolvedSubState) && !string.IsNullOrWhiteSpace(resolvedSubState)
                ? resolvedSubState
                : "unknown";
            string result = values.TryGetValue("Result", out string? resolvedResult) && !string.IsNullOrWhiteSpace(resolvedResult)
                ? resolvedResult
                : "unknown";
            string unitFileState = values.TryGetValue("UnitFileState", out string? resolvedUnitFileState) && !string.IsNullOrWhiteSpace(resolvedUnitFileState)
                ? resolvedUnitFileState
                : "unknown";
            string activeEnterTimestamp = values.TryGetValue("ActiveEnterTimestamp", out string? resolvedActiveEnterTimestamp) && !string.IsNullOrWhiteSpace(resolvedActiveEnterTimestamp)
                ? resolvedActiveEnterTimestamp
                : string.Empty;

            DateTimeOffset? activeSinceUtc = AsaServiceStatusFactory.TryParseSystemdTimestamp(activeEnterTimestamp);
            AsaServiceStatus status = AsaServiceStatusFactory.Create(activeState, subState, result, unitFileState, activeSinceUtc);
            return missingItems.Count > 0 ? status with { CanStart = false } : status;
        }
        catch
        {
            if (File.Exists(InstallStateConstants.ServiceFilePath))
            {
                AsaServiceStatus status = AsaServiceStatusFactory.Create("inactive", "dead", "unknown", "unknown", null);
                return missingItems.Count > 0 ? status with { CanStart = false } : status;
            }

            return AsaServiceStatus.Unknown("Unavailable");
        }
    }

    public IReadOnlyList<string> GetMissingValidationItems()
    {
        List<string> missingItems = [];

        if (!HasInstalledProton())
        {
            missingItems.Add("Proton");
        }

        if (!HasSteamInstall())
        {
            missingItems.Add("SteamCMD");
        }

        if (!File.Exists(InstallStateConstants.StartScriptPath))
        {
            missingItems.Add("Start script");
        }

        if (!File.Exists(InstallStateConstants.ServiceFilePath))
        {
            missingItems.Add("asa.service");
        }

        if (!File.Exists(ProtonConfigConstants.ProtonEnvFilePath))
        {
            missingItems.Add("proton.env");
        }

        if (!File.Exists(ServerConfigConstants.EnvFilePath))
        {
            missingItems.Add("asa.env");
        }

        return missingItems;
    }

    private InstallToolState BuildSteamState()
    {
        if (!File.Exists(InstallStateConstants.SteamCmdPath))
        {
            return new InstallToolState(
                "SteamCMD",
                "Downloads and updates the dedicated server files under /opt/asa/server.",
                "FAILED",
                "null",
                "Missing",
                null,
                InstallStateConstants.SteamRootPath,
                true,
                false);
        }
        return new InstallToolState(
            "SteamCMD",
            "Downloads and updates the dedicated server files under /opt/asa/server.",
            "OK",
            "Installed",
            null,
            null,
            InstallStateConstants.SteamCmdPath,
            true,
            false);
    }

    public static bool HasSteamInstall()
    {
        return File.Exists(Path.Combine(InstallStateConstants.SteamRootPath, "steamcmd.sh")) &&
               Directory.Exists(Path.Combine(InstallStateConstants.SteamRootPath, "linux32"));
    }

    public static bool HasInstalledProton()
    {
        if (!Directory.Exists(InstallStateConstants.ProtonRootPath))
        {
            return false;
        }

        return Directory.GetDirectories(InstallStateConstants.ProtonRootPath, "*", SearchOption.TopDirectoryOnly)
            .Any(directoryPath => File.Exists(Path.Combine(directoryPath, "proton")));
    }

    private async Task<InstallFileState> LoadFileStateAsync(
        string title,
        string description,
        string filePath,
        string templateFileName,
        CancellationToken cancellationToken)
    {
        if (File.Exists(filePath))
        {
            string content = await File.ReadAllTextAsync(filePath, cancellationToken);
            return new InstallFileState(
                title,
                description,
                "OK",
                "Installed",
                filePath,
                content);
        }

        string templateContent = await LoadTemplateAsync(templateFileName, cancellationToken);
        return new InstallFileState(
            title,
            description,
            "Missing",
            title == "Start script" ? "Script missing" : "File missing",
            filePath,
            templateContent);
    }

    private async Task<string> LoadTemplateAsync(string templateFileName, CancellationToken cancellationToken)
    {
        string templatePath = Path.Combine(_environment.ContentRootPath, "Templates", "Install", templateFileName);
        if (!File.Exists(templatePath))
        {
            _logger.LogWarning("Install template was not found at {TemplatePath}.", templatePath);
            return string.Empty;
        }

        return await File.ReadAllTextAsync(templatePath, cancellationToken);
    }

    private static async Task SaveFileAsync(
        string filePath,
        string content,
        bool makeExecutable,
        CancellationToken cancellationToken)
    {
        string? directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await File.WriteAllTextAsync(filePath, content.Replace("\r\n", "\n"), cancellationToken);

        if (makeExecutable)
        {
            await RunProcessAsync("/usr/bin/env", ["chmod", "+x", filePath], cancellationToken);
        }
    }

    private static async Task RunProcessAsync(
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
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                string error = await process.StandardError.ReadToEndAsync(cancellationToken);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Command failed." : error.Trim());
            }
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task<string> RunProcessForOutputAsync(
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

            string output = await standardOutputTask;
            string error = await standardErrorTask;

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Command failed." : error.Trim());
            }

            return output;
        }
        finally
        {
            process.Dispose();
        }
    }

    private static string? ReadSteamVersionLabel()
    {
        string versionFilePath = Path.Combine(InstallStateConstants.SteamRootPath, "version.txt");
        if (!File.Exists(versionFilePath))
        {
            return null;
        }

        string version = File.ReadAllText(versionFilePath).Trim();
        return string.IsNullOrWhiteSpace(version) ? null : $"V{version}";
    }

}
