using System.Net.Http.Json;
using asa_server_node_api.Constants;
using asa_server_node_api.Models.Asa;
using asa_server_node_api.Models.Install;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;

namespace asa_server_node_api.Services;

public sealed class InstallStateService(
    IWebHostEnvironment environment,
    IHttpClientFactory httpClientFactory,
    ILogger<InstallStateService> logger,
    ProtonConfigService protonConfigService)
{
    private readonly IWebHostEnvironment _environment = environment;
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<InstallStateService> _logger = logger;
    private readonly ProtonConfigService _protonConfigService = protonConfigService;

    public bool IsInstallingClusterClient { get; private set; }

    public async Task<InstallWorkspaceSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            InstallToolState proton = await BuildProtonStateAsync(cancellationToken);
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

    public Task<InstallToolState> LoadProtonStateAsync(CancellationToken cancellationToken = default)
    {
        return BuildProtonStateAsync(cancellationToken);
    }

    public Task<InstallToolState> LoadSteamStateAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(BuildSteamState());
    }

    public async Task<ProtonReleaseState> CheckProtonReleaseAsync(CancellationToken cancellationToken = default)
    {
        ProtonRelease release = await GetLatestProtonReleaseAsync(cancellationToken);
        string? currentDirectoryPath = await GetInstalledProtonDirectoryPathAsync(cancellationToken);
        string currentVersion = string.IsNullOrWhiteSpace(currentDirectoryPath)
            ? "Missing"
            : Path.GetFileName(currentDirectoryPath);

        bool updateAvailable = !string.Equals(currentVersion, release.Version, StringComparison.OrdinalIgnoreCase);

        return new ProtonReleaseState(
            currentVersion,
            release.Version,
            release.DownloadUrl,
            updateAvailable);
    }

    public async Task<IReadOnlyList<string>> GetProtonVersionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitHubReleaseResponse> releases = await GetProtonReleasesAsync(cancellationToken);
        return releases
            .Where(release => !string.IsNullOrWhiteSpace(release.TagName))
            .Select(release => release.TagName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> UpdateProtonAsync(string? selectedVersion, CancellationToken cancellationToken = default)
    {
        ProtonRelease release = await ResolveProtonReleaseAsync(selectedVersion, cancellationToken);

        Directory.CreateDirectory(InstallStateConstants.ProtonRootPath);

        string targetDirectoryPath = Path.Combine(InstallStateConstants.ProtonRootPath, release.Version);
        string targetFilesDirectoryPath = Path.Combine(targetDirectoryPath, "files");
        if (Directory.Exists(targetFilesDirectoryPath))
        {
            await _protonConfigService.SaveVersionAsync(release.Version, cancellationToken);
            return $"Proton {release.Version} is already installed.";
        }

        try
        {
            Directory.CreateDirectory(targetDirectoryPath);

            string archivePath = Path.Combine(targetDirectoryPath, $"{release.Version}.tar.gz");
            await RunProcessAsync(
                "/usr/bin/env",
                ["wget", "-q", "-O", archivePath, release.DownloadUrl],
                cancellationToken);

            await RunProcessAsync(
                "/usr/bin/env",
                ["tar", "-xzf", archivePath, "-C", targetDirectoryPath, "--strip-components=1"],
                cancellationToken);

            File.Delete(archivePath);

            if (!Directory.Exists(targetFilesDirectoryPath))
            {
                throw new InvalidOperationException($"Proton install for {release.Version} did not create the expected files directory.");
            }

            await _protonConfigService.SaveVersionAsync(release.Version, cancellationToken);

            return $"Installed Proton {release.Version}.";
        }
        catch
        {
            if (Directory.Exists(targetDirectoryPath))
            {
                Directory.Delete(targetDirectoryPath, recursive: true);
            }

            throw;
        }
    }

    public async Task<string> UninstallProtonAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(InstallStateConstants.ProtonRootPath))
        {
            return "Proton is already missing.";
        }

        string[] installedDirectoryPaths = Directory.GetDirectories(InstallStateConstants.ProtonRootPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Directory.Exists(Path.Combine(path, "files")) || File.Exists(Path.Combine(path, "proton")))
            .ToArray();

        if (installedDirectoryPaths.Length == 0)
        {
            await _protonConfigService.DeleteAsync(cancellationToken);
            return "Proton is already missing.";
        }

        foreach (string installedDirectoryPath in installedDirectoryPaths)
        {
            Directory.Delete(installedDirectoryPath, recursive: true);
        }

        await _protonConfigService.DeleteAsync(cancellationToken);
        return installedDirectoryPaths.Length == 1
            ? $"Uninstalled Proton {Path.GetFileName(installedDirectoryPaths[0])}."
            : $"Uninstalled {installedDirectoryPaths.Length} Proton versions.";
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

    public async Task<string> RestartAsaIfRunningAsync(CancellationToken cancellationToken = default)
    {
        AsaServiceStatus status = await GetAsaServiceStatusAsync(cancellationToken);
        if (!string.Equals(status.ActiveState, "active", StringComparison.Ordinal))
        {
            return $"asa is not running. Current state: {status.DisplayText}. No restart was performed.";
        }

        return await RestartAsaServiceAsync(cancellationToken);
    }

    public bool HasWireGuardClientInstall()
    {
        return File.Exists("/usr/bin/wg") &&
               File.Exists("/usr/bin/wg-quick");
    }

    public bool HasNfsClientInstall()
    {
        return File.Exists("/sbin/mount.nfs") ||
               File.Exists("/usr/sbin/mount.nfs");
    }

    public bool HasClusterClientInstall()
    {
        return HasWireGuardClientInstall() && HasNfsClientInstall();
    }

    public async Task<string> InstallClusterClientAsync(CancellationToken cancellationToken = default)
    {
        if (IsInstallingClusterClient)
        {
            throw new InvalidOperationException("Cluster client install is already running.");
        }

        IsInstallingClusterClient = true;

        try
        {
            await RunProcessAsync(
                SystemCommandConstants.SudoPath,
                ["-n", InstallStateConstants.PrepareClusterClientScriptPath],
                cancellationToken);

            return "Installed cluster client tools. This node is ready to receive WireGuard and NFS configuration.";
        }
        finally
        {
            IsInstallingClusterClient = false;
        }
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

    private async Task<InstallToolState> BuildProtonStateAsync(CancellationToken cancellationToken)
    {
        string? currentDirectoryPath = await GetInstalledProtonDirectoryPathAsync(cancellationToken);
        ProtonRelease? latestRelease = await TryGetLatestProtonReleaseAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(currentDirectoryPath))
        {
            string missingStateLabel = "Missing";
            return new InstallToolState(
                "Proton",
                "Runs the Windows ASA dedicated server build on Linux.",
                "FAILED",
                missingStateLabel,
                "Missing",
                latestRelease is null ? null : $"V{ExtractVersionLabel(latestRelease.Version)}",
                InstallStateConstants.ProtonRootPath,
                latestRelease is not null,
                false);
        }

        string currentVersion = Path.GetFileName(currentDirectoryPath);
        string versionLabel = $"V{ExtractVersionLabel(currentVersion)}";
        string? latestVersionLabel = latestRelease is null ? null : $"V{ExtractVersionLabel(latestRelease.Version)}";
        bool updateAvailable = latestRelease is not null &&
                               !string.Equals(currentVersion, latestRelease.Version, StringComparison.OrdinalIgnoreCase);

        string stateLabel = updateAvailable ? "Update available" : "Ready";

        return new InstallToolState(
            "Proton",
            "Runs the Windows ASA dedicated server build on Linux.",
            "OK",
            stateLabel,
            versionLabel,
            latestVersionLabel,
            currentDirectoryPath,
            latestRelease is not null,
            false);
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

    private async Task<ProtonRelease> GetLatestProtonReleaseAsync(CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient("proton-ge");
        GitHubReleaseResponse? release = await client.GetFromJsonAsync<GitHubReleaseResponse>(
            "https://api.github.com/repos/GloriousEggroll/proton-ge-custom/releases/latest",
            cancellationToken);

        if (release is null || string.IsNullOrWhiteSpace(release.TagName))
        {
            throw new InvalidOperationException("GitHub did not return a valid Proton release.");
        }

        string downloadUrl = release.Assets?
            .FirstOrDefault(asset => string.Equals(asset.Name, $"{release.TagName}.tar.gz", StringComparison.OrdinalIgnoreCase))
            ?.BrowserDownloadUrl
            ?? $"https://github.com/GloriousEggroll/proton-ge-custom/releases/download/{release.TagName}/{release.TagName}.tar.gz";

        return new ProtonRelease(release.TagName, downloadUrl);
    }

    private async Task<IReadOnlyList<GitHubReleaseResponse>> GetProtonReleasesAsync(CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient("proton-ge");
        IReadOnlyList<GitHubReleaseResponse>? releases = await client.GetFromJsonAsync<IReadOnlyList<GitHubReleaseResponse>>(
            "https://api.github.com/repos/GloriousEggroll/proton-ge-custom/releases",
            cancellationToken);

        if (releases is null || releases.Count == 0)
        {
            throw new InvalidOperationException("GitHub did not return any Proton releases.");
        }

        return releases;
    }

    private async Task<ProtonRelease?> TryGetLatestProtonReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetLatestProtonReleaseAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to query latest Proton release.");
            return null;
        }
    }

    private async Task<ProtonRelease> ResolveProtonReleaseAsync(string? selectedVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedVersion))
        {
            return await GetLatestProtonReleaseAsync(cancellationToken);
        }

        return new ProtonRelease(
            selectedVersion,
            $"https://github.com/GloriousEggroll/proton-ge-custom/releases/download/{selectedVersion}/{selectedVersion}.tar.gz");
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

    private async Task<string?> GetInstalledProtonDirectoryPathAsync(CancellationToken cancellationToken)
    {
        string configuredVersion = await _protonConfigService.LoadVersionAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(configuredVersion))
        {
            string configuredDirectoryPath = Path.Combine(InstallStateConstants.ProtonRootPath, configuredVersion);
            if (Directory.Exists(Path.Combine(configuredDirectoryPath, "files")) || File.Exists(Path.Combine(configuredDirectoryPath, "proton")))
            {
                return configuredDirectoryPath;
            }
        }

        return GetCurrentProtonDirectoryPath();
    }

    private static string? GetCurrentProtonDirectoryPath()
    {
        if (!Directory.Exists(InstallStateConstants.ProtonRootPath))
        {
            return null;
        }

        return Directory.GetDirectories(InstallStateConstants.ProtonRootPath, "*", SearchOption.TopDirectoryOnly)
            .Where(path => !path.EndsWith(".old", StringComparison.OrdinalIgnoreCase))
            .Where(path => Directory.Exists(Path.Combine(path, "files")) || File.Exists(Path.Combine(path, "proton")))
            .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
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

    private static string ExtractVersionLabel(string directoryName)
    {
        string sanitized = directoryName.EndsWith(".old", StringComparison.OrdinalIgnoreCase)
            ? directoryName[..^4]
            : directoryName;

        if (sanitized.StartsWith("GE-Proton", StringComparison.OrdinalIgnoreCase))
        {
            return sanitized["GE-Proton".Length..];
        }

        return sanitized;
    }

}
