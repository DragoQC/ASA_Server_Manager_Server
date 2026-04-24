using System.Net.Http.Json;
using asa_server_node_api.Constants;
using asa_server_node_api.Models.Install;
using Microsoft.Extensions.Logging;

namespace asa_server_node_api.Services;

public sealed class ProtonInstallService(
    IHttpClientFactory httpClientFactory,
    ILogger<ProtonInstallService> logger,
    ProtonConfigService protonConfigService)
{
    private readonly IHttpClientFactory _httpClientFactory = httpClientFactory;
    private readonly ILogger<ProtonInstallService> _logger = logger;
    private readonly ProtonConfigService _protonConfigService = protonConfigService;

    public bool IsUpdating { get; private set; }

    public string? ActiveAction { get; private set; }

    public Task<InstallToolState> LoadStateAsync(CancellationToken cancellationToken = default)
    {
        return BuildStateAsync(cancellationToken);
    }

    public async Task<ProtonReleaseState> CheckReleaseAsync(CancellationToken cancellationToken = default)
    {
        ProtonRelease release = await GetLatestReleaseAsync(cancellationToken);
        string? currentDirectoryPath = await GetInstalledDirectoryPathAsync(cancellationToken);
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

    public async Task<IReadOnlyList<string>> GetVersionsAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<GitHubReleaseResponse> releases = await GetReleasesAsync(cancellationToken);
        return releases
            .Where(release => !string.IsNullOrWhiteSpace(release.TagName))
            .Select(release => release.TagName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<string> UpdateAsync(string? selectedVersion, CancellationToken cancellationToken = default)
    {
        if (IsUpdating)
        {
            throw new InvalidOperationException("Proton install is already running.");
        }

        IsUpdating = true;
        ActiveAction = "install";

        try
        {
            ProtonRelease release = await ResolveReleaseAsync(selectedVersion, cancellationToken);

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
        finally
        {
            ActiveAction = null;
            IsUpdating = false;
        }
    }

    public async Task<string> UninstallAsync(CancellationToken cancellationToken = default)
    {
        if (IsUpdating)
        {
            throw new InvalidOperationException("Proton install is already running.");
        }

        IsUpdating = true;
        ActiveAction = "uninstall";

        try
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
        finally
        {
            ActiveAction = null;
            IsUpdating = false;
        }
    }

    private async Task<InstallToolState> BuildStateAsync(CancellationToken cancellationToken)
    {
        string? currentDirectoryPath = await GetInstalledDirectoryPathAsync(cancellationToken);
        ProtonRelease? latestRelease = await TryGetLatestReleaseAsync(cancellationToken);

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

    private async Task<ProtonRelease> GetLatestReleaseAsync(CancellationToken cancellationToken)
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

    private async Task<IReadOnlyList<GitHubReleaseResponse>> GetReleasesAsync(CancellationToken cancellationToken)
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

    private async Task<ProtonRelease?> TryGetLatestReleaseAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await GetLatestReleaseAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Failed to query latest Proton release.");
            return null;
        }
    }

    private async Task<ProtonRelease> ResolveReleaseAsync(string? selectedVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(selectedVersion))
        {
            return await GetLatestReleaseAsync(cancellationToken);
        }

        return new ProtonRelease(
            selectedVersion,
            $"https://github.com/GloriousEggroll/proton-ge-custom/releases/download/{selectedVersion}/{selectedVersion}.tar.gz");
    }

    private async Task<string?> GetInstalledDirectoryPathAsync(CancellationToken cancellationToken)
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

        return GetCurrentDirectoryPath();
    }

    private static string? GetCurrentDirectoryPath()
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
}
