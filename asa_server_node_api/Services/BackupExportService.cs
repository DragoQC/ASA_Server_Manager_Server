using System.Diagnostics;
using System.IO.Compression;
using asa_server_node_api.Constants;
using asa_server_node_api.Models;

namespace asa_server_node_api.Services;

public sealed class BackupExportService(InstallStateService installStateService)
{
    private const string ZipFormat = "zip";
    private const string TarGzFormat = "tar.gz";
    private const long MaxImportBytes = 200L * 1024L * 1024L * 1024L;
    private static readonly string[] ZipToolPaths = ["/usr/bin/zip", "/bin/zip"];
    private static readonly string[] UnzipToolPaths = ["/usr/bin/unzip", "/bin/unzip"];
    private static readonly string[] TarToolPaths = ["/usr/bin/tar", "/bin/tar"];
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(3);
    private readonly InstallStateService _installStateService = installStateService;

    public bool HasZipTools() =>
        ResolveToolPath(ZipToolPaths) is not null &&
        ResolveToolPath(UnzipToolPaths) is not null;

    public bool HasTarTools() => ResolveToolPath(TarToolPaths) is not null;

    public async Task<string> InstallZipToolsAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", InstallStateConstants.PrepareZipToolsScriptPath],
            cancellationToken);

        return "Zip tools installed. Zip backup and restore are ready.";
    }

    public async Task<string> InstallTarToolsAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", InstallStateConstants.PrepareTarToolsScriptPath],
            cancellationToken);

        return "Tar tools installed. Tar.gz backup and restore are ready.";
    }

    public async Task<BackupArchiveInfo> CreateZipArchiveAsync(CancellationToken cancellationToken = default)
    {
        string zipPath = RequireTool(ZipToolPaths, "zip");
        await StopAsaUntilSafeAsync(cancellationToken);
        Directory.CreateDirectory(InstallStateConstants.BackupRootPath);

        string archivePath = BuildArchivePath("zip");
        try
        {
            await RunProcessAsync(
                zipPath,
                ["-r", archivePath, "server"],
                cancellationToken,
                InstallStateConstants.BaseDirectoryPath);
        }
        catch
        {
            DeletePartialArchive(archivePath);
            throw;
        }

        return ToArchiveInfo(ZipFormat, archivePath);
    }

    public async Task<BackupArchiveInfo> CreateTarGzArchiveAsync(CancellationToken cancellationToken = default)
    {
        string tarPath = RequireTool(TarToolPaths, "tar");
        await StopAsaUntilSafeAsync(cancellationToken);
        Directory.CreateDirectory(InstallStateConstants.BackupRootPath);

        string archivePath = BuildArchivePath("tar.gz");
        try
        {
            await RunProcessAsync(
                tarPath,
                ["-czf", archivePath, "-C", InstallStateConstants.BaseDirectoryPath, "server"],
                cancellationToken);
        }
        catch
        {
            DeletePartialArchive(archivePath);
            throw;
        }

        return ToArchiveInfo(TarGzFormat, archivePath);
    }

    public async Task<BackupImportPreview> SaveImportArchiveAsync(
        string fileName,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        string format = GetFormat(fileName);
        RequireFormatTool(format);
        Directory.CreateDirectory(InstallStateConstants.BackupImportRootPath);

        string archivePath = Path.Combine(
            InstallStateConstants.BackupImportRootPath,
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{SanitizeFileName(fileName)}");

        await using (FileStream fileStream = new(
                         archivePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        return await CreateImportPreviewAsync(archivePath, format, cancellationToken);
    }

    public async Task<BackupImportPreview> CreateImportPreviewAsync(
        string archivePath,
        string format,
        CancellationToken cancellationToken = default)
    {
        FileInfo fileInfo = new(archivePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Import archive was not found.", archivePath);
        }

        if (fileInfo.Length > MaxImportBytes)
        {
            throw new InvalidOperationException("Import archive is too large.");
        }

        IReadOnlyList<string> entries = format switch
        {
            ZipFormat => ListZipEntries(archivePath),
            TarGzFormat => await ListTarGzEntriesAsync(archivePath, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported backup format.")
        };

        ValidateArchiveEntries(entries);

        return new BackupImportPreview(
            format,
            fileInfo.Name,
            fileInfo.FullName,
            fileInfo.Length,
            entries.Count,
            entries.Take(25).ToArray(),
            InstallStateConstants.ServerRootPath);
    }

    public async Task<string> RestoreImportArchiveAsync(BackupImportPreview preview, CancellationToken cancellationToken = default)
    {
        RequireFormatTool(preview.Format);
        await StopAsaUntilSafeAsync(cancellationToken, requireServerDirectory: false);

        string restoreId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string workPath = Path.Combine(InstallStateConstants.BackupRestoreWorkRootPath, restoreId);
        string extractPath = Path.Combine(workPath, "extract");
        Directory.CreateDirectory(extractPath);

        try
        {
            if (preview.Format == ZipFormat)
            {
                string unzipPath = RequireTool(UnzipToolPaths, "unzip");
                await RunProcessAsync(
                    unzipPath,
                    ["-q", preview.ArchivePath, "-d", extractPath],
                    cancellationToken);
            }
            else
            {
                string tarPath = RequireTool(TarToolPaths, "tar");
                await RunProcessAsync(
                    tarPath,
                    ["--no-same-owner", "--no-same-permissions", "-xzf", preview.ArchivePath, "-C", extractPath],
                    cancellationToken);
            }

            string sourcePath = ResolveRestoredServerSourcePath(extractPath);
            Directory.CreateDirectory(InstallStateConstants.BackupRootPath);

            string previousServerBackupPath = Path.Combine(
                InstallStateConstants.BackupRootPath,
                $"pre-restore-server-{restoreId}");

            if (Directory.Exists(InstallStateConstants.ServerRootPath))
            {
                Directory.Move(InstallStateConstants.ServerRootPath, previousServerBackupPath);
            }

            Directory.Move(sourcePath, InstallStateConstants.ServerRootPath);
            return Directory.Exists(previousServerBackupPath)
                ? $"Restore completed. Previous server folder saved at {previousServerBackupPath}. asa.service was left stopped."
                : "Restore completed. asa.service was left stopped.";
        }
        finally
        {
            DeleteDirectoryIfExists(workPath);
        }
    }

    public BackupArchiveInfo? GetLatestArchive(string format)
    {
        string searchPattern = format switch
        {
            ZipFormat => "asa-server-*.zip",
            TarGzFormat => "asa-server-*.tar.gz",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(searchPattern) || !Directory.Exists(InstallStateConstants.BackupRootPath))
        {
            return null;
        }

        FileInfo? latestFile = new DirectoryInfo(InstallStateConstants.BackupRootPath)
            .EnumerateFiles(searchPattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        return latestFile is null ? null : ToArchiveInfo(format, latestFile.FullName);
    }

    private async Task StopAsaUntilSafeAsync(CancellationToken cancellationToken, bool requireServerDirectory = true)
    {
        if (requireServerDirectory && !Directory.Exists(InstallStateConstants.ServerRootPath))
        {
            throw new DirectoryNotFoundException($"{InstallStateConstants.ServerRootPath} does not exist.");
        }

        Models.Asa.AsaServiceStatus status = await _installStateService.GetAsaServiceStatusAsync(cancellationToken);
        if (status.IsUnavailable)
        {
            throw new InvalidOperationException("asa service status is unavailable. Backup cannot verify the server is stopped.");
        }

        if (status.CanStop)
        {
            await _installStateService.StopAsaServiceAsync(cancellationToken);
        }
        else if (!status.IsStopped && !status.IsFailed)
        {
            throw new InvalidOperationException($"asa cannot be stopped while it is {status.DisplayText.ToLowerInvariant()}.");
        }

        DateTimeOffset stopDeadline = DateTimeOffset.UtcNow.Add(StopTimeout);
        do
        {
            status = await _installStateService.GetAsaServiceStatusAsync(cancellationToken);
            if (status.IsStopped || status.IsFailed)
            {
                return;
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        while (DateTimeOffset.UtcNow < stopDeadline);

        throw new TimeoutException("Timed out waiting for asa to stop.");
    }

    private static string BuildArchivePath(string extension)
    {
        string timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        return Path.Combine(InstallStateConstants.BackupRootPath, $"asa-server-{timestamp}.{extension}");
    }

    private static string GetFormat(string fileName)
    {
        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ZipFormat;
        }

        if (fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
            fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase))
        {
            return TarGzFormat;
        }

        throw new InvalidOperationException("Upload a .zip, .tar.gz, or .tgz backup archive.");
    }

    private static IReadOnlyList<string> ListZipEntries(string archivePath)
    {
        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        return archive.Entries
            .Select(entry => entry.FullName.Replace('\\', '/'))
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .ToArray();
    }

    private static async Task<IReadOnlyList<string>> ListTarGzEntriesAsync(string archivePath, CancellationToken cancellationToken)
    {
        string tarPath = RequireTool(TarToolPaths, "tar");
        string output = await RunProcessForOutputAsync(tarPath, ["-tzf", archivePath], cancellationToken);
        return output.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToArray();
    }

    private static void ValidateArchiveEntries(IReadOnlyList<string> entries)
    {
        if (entries.Count == 0)
        {
            throw new InvalidOperationException("Archive is empty.");
        }

        foreach (string entry in entries)
        {
            string normalizedEntry = entry.Replace('\\', '/');
            if (normalizedEntry.StartsWith("/", StringComparison.Ordinal) ||
                normalizedEntry.Contains("../", StringComparison.Ordinal) ||
                normalizedEntry.Equals("..", StringComparison.Ordinal) ||
                normalizedEntry.Contains('\0'))
            {
                throw new InvalidOperationException($"Archive contains unsafe path: {entry}");
            }
        }
    }

    private static string ResolveRestoredServerSourcePath(string extractPath)
    {
        string nestedServerPath = Path.Combine(extractPath, "server");
        if (Directory.Exists(nestedServerPath))
        {
            return nestedServerPath;
        }

        if (File.Exists(Path.Combine(extractPath, "asa.env")) ||
            Directory.Exists(Path.Combine(extractPath, "ShooterGame")))
        {
            return extractPath;
        }

        throw new InvalidOperationException("Archive must contain a server folder, asa.env, or ShooterGame.");
    }

    private static string SanitizeFileName(string fileName)
    {
        string sanitized = Path.GetFileName(fileName);
        foreach (char invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidChar, '-');
        }

        return string.IsNullOrWhiteSpace(sanitized) ? "backup-archive" : sanitized;
    }

    private static BackupArchiveInfo ToArchiveInfo(string format, string archivePath)
    {
        FileInfo fileInfo = new(archivePath);
        return new BackupArchiveInfo(format, fileInfo.Name, fileInfo.FullName, fileInfo.Length, fileInfo.LastWriteTimeUtc);
    }

    private static void DeletePartialArchive(string archivePath)
    {
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }
    }

    private static void DeleteDirectoryIfExists(string directoryPath)
    {
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static void RequireFormatTool(string format)
    {
        if (format == ZipFormat)
        {
            RequireTool(UnzipToolPaths, "unzip");
            return;
        }

        if (format == TarGzFormat)
        {
            RequireTool(TarToolPaths, "tar");
            return;
        }

        throw new InvalidOperationException("Unsupported backup format.");
    }

    private static string RequireTool(IReadOnlyList<string> toolPaths, string toolName)
    {
        return ResolveToolPath(toolPaths)
            ?? throw new InvalidOperationException($"{toolName} is not installed. Prepare {toolName} tools first.");
    }

    private static string? ResolveToolPath(IReadOnlyList<string> toolPaths)
    {
        return toolPaths.FirstOrDefault(File.Exists);
    }

    private static async Task RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken,
        string? workingDirectory = null)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory ?? string.Empty
            }
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            string error = await standardErrorTask;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Archive command failed." : error.Trim());
        }
    }

    private static async Task<string> RunProcessForOutputAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo
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

        process.Start();
        Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        string output = await standardOutputTask;
        string error = await standardErrorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Archive command failed." : error.Trim());
        }

        return output;
    }
}
