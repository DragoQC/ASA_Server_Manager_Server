using System.Diagnostics;
using System.IO.Compression;
using asa_server_node_api.Constants;
using asa_server_node_api.Models;

namespace asa_server_node_api.Services;

public sealed class BackupService(InstallStateService installStateService)
{
    private const string ZipFormat = "zip";
    private const string TarGzFormat = "tar.gz";
    private const long MaxImportBytes = 200L * 1024L * 1024L * 1024L;
    private static readonly string[] ZipToolPaths = ["/usr/bin/zip", "/bin/zip"];
    private static readonly string[] UnzipToolPaths = ["/usr/bin/unzip", "/bin/unzip"];
    private static readonly string[] TarToolPaths = ["/usr/bin/tar", "/bin/tar"];
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(3);
    private readonly InstallStateService _installStateService = installStateService;

    public event Action? Changed;

    public bool HasZipTools { get; private set; }
    public bool HasTarTools { get; private set; }
    public BackupArchiveInfo? ZipArchive { get; private set; }
    public BackupArchiveInfo? TarGzArchive { get; private set; }
    public bool IsCreatingZip { get; private set; }
    public bool IsCreatingTarGz { get; private set; }
    public bool IsUploadingRestore { get; private set; }
    public bool IsRestoring { get; private set; }
    public string? ExportProgressText { get; private set; }
    public double? ExportProgressPercent { get; private set; }
    public string? RestoreSelectedFileName { get; private set; }
    public string? RestoreProgressText { get; private set; }
    public BackupImportPreview? RestorePreview { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RefreshToolState();
        LoadArchives();
        NotifyChanged();
        return Task.CompletedTask;
    }

    public async Task<string> InstallZipToolsAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", InstallStateConstants.PrepareZipToolsScriptPath],
            cancellationToken);

        RefreshToolState();
        LoadArchives();
        NotifyChanged();
        return "Zip tools installed. Zip backup and restore are ready.";
    }

    public async Task<string> InstallTarToolsAsync(CancellationToken cancellationToken = default)
    {
        await RunProcessAsync(
            SystemCommandConstants.SudoPath,
            ["-n", InstallStateConstants.PrepareTarToolsScriptPath],
            cancellationToken);

        RefreshToolState();
        LoadArchives();
        NotifyChanged();
        return "Tar tools installed. Tar.gz backup and restore are ready.";
    }

    public async Task<BackupArchiveInfo> CreateZipArchiveAsync(CancellationToken cancellationToken = default)
    {
        StartZipExport("Starting zip backup...");
        try
        {
            ZipArchive = await CreateZipArchiveCoreAsync(cancellationToken);
            LoadArchives();
            FinishExport("Zip archive ready. Download is available.");
            return ZipArchive!;
        }
        catch
        {
            FailExport();
            throw;
        }
    }

    public async Task<BackupArchiveInfo> CreateTarGzArchiveAsync(CancellationToken cancellationToken = default)
    {
        StartTarGzExport("Starting tar.gz backup...");
        try
        {
            TarGzArchive = await CreateTarGzArchiveCoreAsync(cancellationToken);
            LoadArchives();
            FinishExport("Tar.gz archive ready. Download is available.");
            return TarGzArchive!;
        }
        catch
        {
            FailExport();
            throw;
        }
    }

    public bool NeedsMissingRestoreTool(string fileName, out string? errorMessage, out bool openZipDialog, out bool openTarDialog)
    {
        openZipDialog = false;
        openTarDialog = false;
        errorMessage = null;

        if (fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !HasZipTools)
        {
            errorMessage = "Zip tools are required to restore this archive. Click Prepare zip tools first.";
            openZipDialog = true;
            return true;
        }

        if ((fileName.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
             fileName.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase)) &&
            !HasTarTools)
        {
            errorMessage = "Tar tools are required to restore this archive. Click Prepare tar tools first.";
            openTarDialog = true;
            return true;
        }

        return false;
    }

    public async Task<BackupImportPreview> UploadRestoreArchiveAsync(string fileName, Stream stream, CancellationToken cancellationToken = default)
    {
        StartRestoreUpload(fileName, $"Selected {fileName}. Preparing upload...");
        try
        {
            Progress<string> progress = new(UpdateRestoreProgress);
            RestorePreview = await SaveImportArchiveAsync(fileName, stream, progress, cancellationToken);
            SetRestorePreview(RestorePreview, $"Preview ready for {fileName}. Next: review entries, then click Restore archive.");
            return RestorePreview;
        }
        catch
        {
            FailRestore();
            throw;
        }
    }

    public async Task<string> RestoreArchiveAsync(CancellationToken cancellationToken = default)
    {
        if (RestorePreview is null)
        {
            throw new InvalidOperationException("No restore preview is loaded.");
        }

        StartRestore("Restore confirmed. Starting restore flow...");
        try
        {
            Progress<string> progress = new(UpdateRestoreProgress);
            string message = await RestoreImportArchiveAsync(RestorePreview, progress, cancellationToken);
            FinishRestore("Restore completed. asa.service was left stopped.");
            return message;
        }
        catch
        {
            FailRestore();
            throw;
        }
    }

    public void MarkRestoreReadyForConfirmation()
    {
        if (RestorePreview is null)
        {
            return;
        }

        UpdateRestoreProgress($"Ready to restore {RestorePreview.FileName}. Next: confirm restore to replace /opt/asa/server.");
    }

    private bool DetectHasZipTools() =>
        ResolveToolPath(ZipToolPaths) is not null &&
        ResolveToolPath(UnzipToolPaths) is not null;

    private bool DetectHasTarTools() => ResolveToolPath(TarToolPaths) is not null;

    public async Task<BackupArchiveInfo> CreateZipArchiveCoreAsync(
        CancellationToken cancellationToken = default)
    {
        string zipPath = RequireTool(ZipToolPaths, "zip");
        UpdateExportProgress("Stopping asa.service before creating zip backup...", 0D);
        await StopAsaUntilSafeAsync(cancellationToken);
        Directory.CreateDirectory(InstallStateConstants.BackupRootPath);
        ArchiveProgressPlan progressPlan = BuildArchiveProgressPlan(
            InstallStateConstants.ServerRootPath,
            "server");

        string archivePath = BuildArchivePath("zip");
        string temporaryArchivePath = BuildTemporaryArchivePath(archivePath);
        try
        {
            UpdateExportProgress("Creating zip archive from /opt/asa/server...", 0D);
            await RunArchiveProcessWithProgressAsync(
                zipPath,
                ["-r", temporaryArchivePath, "server"],
                InstallStateConstants.BaseDirectoryPath,
                progressPlan,
                ParseZipArchiveOutputPath,
                "Compressing zip archive",
                cancellationToken);

            UpdateExportProgress("Finalizing zip archive...", 100D);
            PromoteCompletedArchive(temporaryArchivePath, archivePath);
        }
        catch
        {
            DeletePartialArchive(temporaryArchivePath);
            DeletePartialArchive(archivePath);
            throw;
        }

        UpdateExportProgress("Zip archive ready. Download is available.", 100D);
        return ToArchiveInfo(ZipFormat, archivePath);
    }

    public async Task<BackupArchiveInfo> CreateTarGzArchiveCoreAsync(
        CancellationToken cancellationToken = default)
    {
        string tarPath = RequireTool(TarToolPaths, "tar");
        UpdateExportProgress("Stopping asa.service before creating tar.gz backup...", 0D);
        await StopAsaUntilSafeAsync(cancellationToken);
        Directory.CreateDirectory(InstallStateConstants.BackupRootPath);
        ArchiveProgressPlan progressPlan = BuildArchiveProgressPlan(
            InstallStateConstants.ServerRootPath,
            "server");

        string archivePath = BuildArchivePath("tar.gz");
        string temporaryArchivePath = BuildTemporaryArchivePath(archivePath);
        try
        {
            UpdateExportProgress("Creating tar.gz archive from /opt/asa/server...", 0D);
            await RunArchiveProcessWithProgressAsync(
                tarPath,
                ["-cvzf", temporaryArchivePath, "server"],
                InstallStateConstants.BaseDirectoryPath,
                progressPlan,
                ParseTarArchiveOutputPath,
                "Compressing tar.gz archive",
                cancellationToken);

            UpdateExportProgress("Finalizing tar.gz archive...", 100D);
            PromoteCompletedArchive(temporaryArchivePath, archivePath);
        }
        catch
        {
            DeletePartialArchive(temporaryArchivePath);
            DeletePartialArchive(archivePath);
            throw;
        }

        UpdateExportProgress("Tar.gz archive ready. Download is available.", 100D);
        return ToArchiveInfo(TarGzFormat, archivePath);
    }

    public async Task<BackupImportPreview> SaveImportArchiveAsync(
        string fileName,
        Stream stream,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string format = GetFormat(fileName);
        RequireFormatTool(format);
        Directory.CreateDirectory(InstallStateConstants.BackupImportRootPath);

        string archivePath = Path.Combine(
            InstallStateConstants.BackupImportRootPath,
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{SanitizeFileName(fileName)}");

        progress?.Report($"Uploading {fileName} to the node...");
        await using (FileStream fileStream = new(
                         archivePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None))
        {
            await stream.CopyToAsync(fileStream, cancellationToken);
        }

        progress?.Report($"Upload finished. Reading {fileName} and building restore preview...");
        return await CreateImportPreviewAsync(archivePath, format, progress, cancellationToken);
    }

    public async Task<BackupImportPreview> CreateImportPreviewAsync(
        string archivePath,
        string format,
        IProgress<string>? progress = null,
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

        progress?.Report($"Validating archive format {format}...");
        IReadOnlyList<string> entries = format switch
        {
            ZipFormat => ListZipEntries(archivePath),
            TarGzFormat => await ListTarGzEntriesAsync(archivePath, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported backup format.")
        };

        progress?.Report("Checking archive entries and restore path...");
        ValidateArchiveEntries(entries);

        progress?.Report($"Preview ready. Found {entries.Count} entries. You can review and restore when ready.");

        return new BackupImportPreview(
            format,
            fileInfo.Name,
            fileInfo.FullName,
            fileInfo.Length,
            entries.Count,
            entries.Take(25).ToArray(),
            InstallStateConstants.ServerRootPath);
    }

    public async Task<string> RestoreImportArchiveAsync(
        BackupImportPreview preview,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        RequireFormatTool(preview.Format);
        progress?.Report("Stopping asa.service before restore...");
        await StopAsaUntilSafeAsync(cancellationToken, requireServerDirectory: false);

        string restoreId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string workPath = Path.Combine(InstallStateConstants.BackupRestoreWorkRootPath, restoreId);
        string extractPath = Path.Combine(workPath, "extract");
        Directory.CreateDirectory(extractPath);

        try
        {
            if (preview.Format == ZipFormat)
            {
                progress?.Report("Extracting zip archive into restore workspace...");
                string unzipPath = RequireTool(UnzipToolPaths, "unzip");
                await RunProcessAsync(
                    unzipPath,
                    ["-q", preview.ArchivePath, "-d", extractPath],
                    cancellationToken);
            }
            else
            {
                progress?.Report("Extracting tar.gz archive into restore workspace...");
                string tarPath = RequireTool(TarToolPaths, "tar");
                await RunProcessAsync(
                    tarPath,
                    ["--no-same-owner", "--no-same-permissions", "-xzf", preview.ArchivePath, "-C", extractPath],
                    cancellationToken);
            }

            progress?.Report("Resolving restored server folder...");
            string sourcePath = ResolveRestoredServerSourcePath(extractPath);
            Directory.CreateDirectory(InstallStateConstants.BackupRootPath);

            string previousServerBackupPath = Path.Combine(
                InstallStateConstants.BackupRootPath,
                $"pre-restore-server-{restoreId}");

            if (Directory.Exists(InstallStateConstants.ServerRootPath))
            {
                progress?.Report("Moving current /opt/asa/server into backup...");
                Directory.Move(InstallStateConstants.ServerRootPath, previousServerBackupPath);
            }

            progress?.Report("Placing restored files into /opt/asa/server...");
            Directory.Move(sourcePath, InstallStateConstants.ServerRootPath);
            progress?.Report("Restore completed. asa.service was left stopped.");
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

    private void RefreshToolState()
    {
        HasZipTools = DetectHasZipTools();
        HasTarTools = DetectHasTarTools();
    }

    private void LoadArchives()
    {
        ZipArchive = GetLatestArchive(ZipFormat);
        TarGzArchive = GetLatestArchive(TarGzFormat);
    }

    private void StartZipExport(string message)
    {
        IsCreatingZip = true;
        IsCreatingTarGz = false;
        ExportProgressText = message;
        ExportProgressPercent = 0D;
        NotifyChanged();
    }

    private void StartTarGzExport(string message)
    {
        IsCreatingZip = false;
        IsCreatingTarGz = true;
        ExportProgressText = message;
        ExportProgressPercent = 0D;
        NotifyChanged();
    }

    private void UpdateExportProgress(string message)
    {
        ExportProgressText = message;
        NotifyChanged();
    }

    private void UpdateExportProgress(string message, double? percent)
    {
        ExportProgressText = message;
        ExportProgressPercent = percent is null ? null : Math.Clamp(percent.Value, 0D, 100D);
        NotifyChanged();
    }

    private void FinishExport(string message)
    {
        IsCreatingZip = false;
        IsCreatingTarGz = false;
        ExportProgressText = message;
        ExportProgressPercent = null;
        RefreshToolState();
        NotifyChanged();
    }

    private void FailExport(string? message = null)
    {
        IsCreatingZip = false;
        IsCreatingTarGz = false;
        if (!string.IsNullOrWhiteSpace(message))
        {
            ExportProgressText = message;
        }

        ExportProgressPercent = null;
        RefreshToolState();
        NotifyChanged();
    }

    private void StartRestoreUpload(string fileName, string message)
    {
        RestoreSelectedFileName = fileName;
        RestorePreview = null;
        IsUploadingRestore = true;
        IsRestoring = false;
        RestoreProgressText = message;
        NotifyChanged();
    }

    private void UpdateRestoreProgress(string message)
    {
        RestoreProgressText = message;
        NotifyChanged();
    }

    private void SetRestorePreview(BackupImportPreview preview, string message)
    {
        RestorePreview = preview;
        IsUploadingRestore = false;
        RestoreProgressText = message;
        NotifyChanged();
    }

    private void StartRestore(string message)
    {
        IsUploadingRestore = false;
        IsRestoring = true;
        RestoreProgressText = message;
        NotifyChanged();
    }

    private void FinishRestore(string message)
    {
        IsRestoring = false;
        RestoreProgressText = message;
        RefreshToolState();
        LoadArchives();
        NotifyChanged();
    }

    private void FailRestore(string? message = null)
    {
        IsUploadingRestore = false;
        IsRestoring = false;
        if (!string.IsNullOrWhiteSpace(message))
        {
            RestoreProgressText = message;
        }

        NotifyChanged();
    }

    private void NotifyChanged()
    {
        Changed?.Invoke();
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

    private static string BuildTemporaryArchivePath(string archivePath)
    {
        return $"{archivePath}.partial";
    }

    private static ArchiveProgressPlan BuildArchiveProgressPlan(string sourceRootPath, string archiveRootName)
    {
        Dictionary<string, long> fileSizes = new(StringComparer.Ordinal);
        long totalBytes = 0;

        foreach (string filePath in Directory.EnumerateFiles(sourceRootPath, "*", SearchOption.AllDirectories))
        {
            string relativePath = Path.GetRelativePath(sourceRootPath, filePath).Replace('\\', '/');
            string archiveEntryPath = $"{archiveRootName}/{relativePath}";
            long sizeBytes = new FileInfo(filePath).Length;
            fileSizes[archiveEntryPath] = sizeBytes;
            totalBytes += sizeBytes;
        }

        return new ArchiveProgressPlan(fileSizes, totalBytes, fileSizes.Count);
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

    private static void PromoteCompletedArchive(string temporaryArchivePath, string archivePath)
    {
        if (!File.Exists(temporaryArchivePath))
        {
            throw new InvalidOperationException("Archive creation did not produce an output file.");
        }

        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        File.Move(temporaryArchivePath, archivePath);
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

    private async Task RunArchiveProcessWithProgressAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        ArchiveProgressPlan progressPlan,
        Func<string, string?> parseEntryPath,
        string operationLabel,
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
                CreateNoWindow = true,
                WorkingDirectory = workingDirectory
            }
        };

        foreach (string argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        using CancellationTokenRegistration registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });

        process.Start();
        Task<string> standardErrorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        HashSet<string> processedEntries = new(StringComparer.Ordinal);
        long processedBytes = 0;
        int processedFiles = 0;

        while (true)
        {
            string? line = await process.StandardOutput.ReadLineAsync().WaitAsync(cancellationToken);
            if (line is null)
            {
                break;
            }

            string? archiveEntryPath = parseEntryPath(line);
            if (string.IsNullOrWhiteSpace(archiveEntryPath) ||
                !progressPlan.FileSizes.TryGetValue(archiveEntryPath, out long sizeBytes) ||
                !processedEntries.Add(archiveEntryPath))
            {
                continue;
            }

            processedBytes += sizeBytes;
            processedFiles++;

            double percent = CalculateArchiveProgressPercent(
                processedBytes,
                progressPlan.TotalBytes,
                processedFiles,
                progressPlan.TotalFiles);

            UpdateExportProgress(
                $"{operationLabel}... {processedFiles}/{progressPlan.TotalFiles} files",
                percent);
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            string error = await standardErrorTask;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Archive command failed." : error.Trim());
        }
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

    private static double CalculateArchiveProgressPercent(long processedBytes, long totalBytes, int processedFiles, int totalFiles)
    {
        if (totalBytes > 0)
        {
            return Math.Clamp((double)processedBytes / totalBytes * 100D, 0D, 100D);
        }

        if (totalFiles > 0)
        {
            return Math.Clamp((double)processedFiles / totalFiles * 100D, 0D, 100D);
        }

        return 100D;
    }

    private static string? ParseZipArchiveOutputPath(string line)
    {
        const string addingPrefix = "adding: ";
        const string updatingPrefix = "updating: ";

        string trimmedLine = line.Trim();
        if (trimmedLine.StartsWith(addingPrefix, StringComparison.Ordinal))
        {
            trimmedLine = trimmedLine[addingPrefix.Length..];
        }
        else if (trimmedLine.StartsWith(updatingPrefix, StringComparison.Ordinal))
        {
            trimmedLine = trimmedLine[updatingPrefix.Length..];
        }
        else
        {
            return null;
        }

        int metadataIndex = trimmedLine.LastIndexOf(" (", StringComparison.Ordinal);
        if (metadataIndex >= 0)
        {
            trimmedLine = trimmedLine[..metadataIndex];
        }

        return NormalizeArchiveOutputPath(trimmedLine);
    }

    private static string? ParseTarArchiveOutputPath(string line)
    {
        return NormalizeArchiveOutputPath(line);
    }

    private static string? NormalizeArchiveOutputPath(string path)
    {
        string normalizedPath = path.Trim().Replace('\\', '/');
        if (normalizedPath.StartsWith("./", StringComparison.Ordinal))
        {
            normalizedPath = normalizedPath[2..];
        }

        return string.IsNullOrWhiteSpace(normalizedPath) ? null : normalizedPath;
    }

    private sealed record ArchiveProgressPlan(
        IReadOnlyDictionary<string, long> FileSizes,
        long TotalBytes,
        int TotalFiles);
}
