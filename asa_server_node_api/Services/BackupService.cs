using System.Diagnostics;
using System.IO.Compression;
using asa_server_node_api.Constants;
using asa_server_node_api.Models;
using Microsoft.Extensions.DependencyInjection;

namespace asa_server_node_api.Services;

public sealed class BackupService(IServiceScopeFactory serviceScopeFactory, ToastService toastService)
{
    private const string ZipFormat = "zip";
    private const string TarGzFormat = "tar.gz";
    private const long MaxImportBytes = 200L * 1024L * 1024L * 1024L;
    private const long ArchiveSpaceOverheadBytes = 256L * 1024L * 1024L;
    private static readonly string[] ZipToolPaths = ["/usr/bin/zip", "/bin/zip"];
    private static readonly string[] UnzipToolPaths = ["/usr/bin/unzip", "/bin/unzip"];
    private static readonly string[] TarToolPaths = ["/usr/bin/tar", "/bin/tar"];
    private static readonly TimeSpan StopTimeout = TimeSpan.FromMinutes(3);
    private readonly IServiceScopeFactory _serviceScopeFactory = serviceScopeFactory;
    private readonly ToastService _toastService = toastService;
    private CancellationTokenSource? _restorePreparationCancellationTokenSource;
    private CancellationTokenSource? _restoreCancellationTokenSource;
    private ArchiveFingerprint? _validatedRestoreArchive;

    public event Action? Changed;

    public bool HasZipTools { get; private set; }
    public bool HasTarTools { get; private set; }
    public BackupArchiveInfo? ZipArchive { get; private set; }
    public BackupArchiveInfo? TarGzArchive { get; private set; }
    public bool IsCreatingZip { get; private set; }
    public bool IsCreatingTarGz { get; private set; }
    public bool IsUploadingRestore { get; private set; }
    public bool IsPreparingRestorePreview { get; private set; }
    public bool IsRestoring { get; private set; }
    public string? ExportProgressText { get; private set; }
    public double? ExportProgressPercent { get; private set; }
    public long? ExportProgressCurrentBytes { get; private set; }
    public long? ExportProgressTotalBytes { get; private set; }
    public string? RestoreSelectedFileName { get; private set; }
    public string? RestoreProgressText { get; private set; }
    public double? RestoreProgressPercent { get; private set; }
    public long? RestoreProgressCurrentBytes { get; private set; }
    public long? RestoreProgressTotalBytes { get; private set; }
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
        _toastService.ShowInfo("Zip backup started.", "Backup");
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
        _toastService.ShowInfo("Tar.gz backup started.", "Backup");
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

    public async Task<BackupImportPreview> UploadRestoreArchiveAsync(
        string fileName,
        long totalBytes,
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        StartRestoreUpload(fileName, $"Selected {fileName}. Preparing upload...");
        using CancellationTokenSource linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _restorePreparationCancellationTokenSource = linkedCancellationTokenSource;
        try
        {
            Progress<string> progress = new(UpdateRestoreProgress);
            RestorePreview = await SaveImportArchiveAsync(fileName, totalBytes, stream, progress, linkedCancellationTokenSource.Token);
            SetRestorePreview(RestorePreview, $"Preview ready for {fileName}. Next: review entries, then click Restore archive.");
            return RestorePreview;
        }
        catch (OperationCanceledException)
        {
            FailRestore("Restore preparation canceled.");
            _toastService.ShowInfo("Restore preparation canceled.", "Restore");
            throw new InvalidOperationException("Restore preparation canceled.");
        }
        catch
        {
            FailRestore();
            throw;
        }
        finally
        {
            _restorePreparationCancellationTokenSource = null;
        }
    }

    public async Task<string> RestoreArchiveAsync(CancellationToken cancellationToken = default)
    {
        if (RestorePreview is null)
        {
            throw new InvalidOperationException("No restore preview is loaded.");
        }

        if (IsRestoring)
        {
            throw new InvalidOperationException("Restore is already running.");
        }

        StartRestore("Restore confirmed. Starting restore flow...");
        _toastService.ShowInfo("Restore started.", "Restore");
        using CancellationTokenSource linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _restoreCancellationTokenSource = linkedCancellationTokenSource;
        try
        {
            Progress<string> progress = new(UpdateRestoreProgress);
            string message = await RestoreImportArchiveAsync(RestorePreview, progress, linkedCancellationTokenSource.Token);
            FinishRestore("Restore completed. asa.service was left stopped.");
            return message;
        }
        catch (OperationCanceledException)
        {
            FailRestore("Restore canceled.");
            _toastService.ShowInfo("Restore canceled.", "Restore");
            throw new InvalidOperationException("Restore canceled.");
        }
        catch
        {
            FailRestore();
            throw;
        }
        finally
        {
            _restoreCancellationTokenSource = null;
        }
    }

    public void CancelRestore()
    {
        _restoreCancellationTokenSource?.Cancel();
    }

    public void MarkRestoreReadyForConfirmation()
    {
        if (RestorePreview is null)
        {
            return;
        }

        UpdateRestoreProgress($"Ready to restore {RestorePreview.FileName}. Next: confirm restore to replace /opt/asa/server.");
    }

    public bool IsLatestArchiveValidatedForRestore(string format)
    {
        BackupArchiveInfo? archive = GetLatestArchive(format);
        return archive is not null && IsArchiveValidatedForRestore(archive);
    }

    public bool TryPrepareLatestArchiveForRestore(string format)
    {
        BackupArchiveInfo? archive = GetLatestArchive(format);
        if (archive is null || RestorePreview is null || !IsArchiveValidatedForRestore(archive))
        {
            return false;
        }

        RestoreSelectedFileName = archive.FileName;
        MarkRestoreReadyForConfirmation();
        return true;
    }

    public Task DeleteLatestArchiveAsync(string format, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BackupArchiveInfo? archive = GetLatestArchive(format);
        if (archive is not null && File.Exists(archive.FilePath))
        {
            File.Delete(archive.FilePath);
        }

        if (archive is not null &&
            (IsArchiveValidatedForRestore(archive) ||
             string.Equals(RestorePreview?.ArchivePath, archive.FilePath, StringComparison.OrdinalIgnoreCase)))
        {
            ClearValidatedRestorePreview();
        }

        LoadArchives();
        NotifyChanged();
        _toastService.ShowSuccess($"{format} backup deleted.", "Backup");
        return Task.CompletedTask;
    }

    public async Task<BackupImportPreview> LoadRestorePreviewFromLatestArchiveAsync(
        string format,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        BackupArchiveInfo? archive = GetLatestArchive(format);
        if (archive is null || !File.Exists(archive.FilePath))
        {
            throw new InvalidOperationException($"No latest {format} backup is available.");
        }

        RestoreSelectedFileName = archive.FileName;
        RestorePreview = null;
        IsUploadingRestore = false;
        IsPreparingRestorePreview = true;
        IsRestoring = false;
        RestoreProgressPercent = null;
        RestoreProgressCurrentBytes = null;
        RestoreProgressTotalBytes = null;
        RestoreProgressText = $"Reading {archive.FileName} and building restore preview...";
        NotifyChanged();

        using CancellationTokenSource linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _restorePreparationCancellationTokenSource = linkedCancellationTokenSource;
        Progress<string> progress = new(UpdateRestoreProgress);
        try
        {
            RestorePreview = await CreateImportPreviewAsync(archive.FilePath, archive.Format, progress, linkedCancellationTokenSource.Token);
            SetRestorePreview(RestorePreview, $"Preview ready for {archive.FileName}. Next: review entries, then click Restore archive.");
            return RestorePreview;
        }
        catch (OperationCanceledException)
        {
            FailRestore("Restore preparation canceled.");
            _toastService.ShowInfo("Restore preparation canceled.", "Restore");
            throw new InvalidOperationException("Restore preparation canceled.");
        }
        finally
        {
            _restorePreparationCancellationTokenSource = null;
        }
    }

    public void CancelRestorePreparation()
    {
        _restorePreparationCancellationTokenSource?.Cancel();
    }

    private bool DetectHasZipTools() =>
        ResolveToolPath(ZipToolPaths) is not null &&
        ResolveToolPath(UnzipToolPaths) is not null;

    private bool DetectHasTarTools() => ResolveToolPath(TarToolPaths) is not null;

    public async Task<BackupArchiveInfo> CreateZipArchiveCoreAsync(
        CancellationToken cancellationToken = default)
    {
        string zipPath = RequireTool(ZipToolPaths, "zip");
        ArchiveProgressPlan progressPlan = BuildArchiveProgressPlan(
            InstallStateConstants.ServerRootPath,
            "server");
        EnsureEnoughFreeSpaceForBackup(ZipFormat, progressPlan.TotalBytes);
        UpdateExportProgress("Stopping asa.service before creating zip backup...", 0D);
        await StopAsaUntilSafeAsync(cancellationToken);
        Directory.CreateDirectory(InstallStateConstants.BackupRootPath);
        DeleteExistingArchives(ZipFormat);

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
        ArchiveProgressPlan progressPlan = BuildArchiveProgressPlan(
            InstallStateConstants.ServerRootPath,
            "server");
        EnsureEnoughFreeSpaceForBackup(TarGzFormat, progressPlan.TotalBytes);
        UpdateExportProgress("Stopping asa.service before creating tar.gz backup...", 0D);
        await StopAsaUntilSafeAsync(cancellationToken);
        Directory.CreateDirectory(InstallStateConstants.BackupRootPath);
        DeleteExistingArchives(TarGzFormat);

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
        long totalBytes,
        Stream stream,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string format = GetFormat(fileName);
        RequireFormatTool(format);
        Directory.CreateDirectory(InstallStateConstants.BackupRootPath);
        DeleteExistingArchives(format);

        string archivePath = BuildArchivePath(format);

        progress?.Report($"Uploading {fileName} to the node...");
        await using (FileStream fileStream = new(
                         archivePath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None))
        {
            await CopyStreamWithProgressAsync(stream, fileStream, totalBytes, cancellationToken);
        }

        StartRestoreValidation($"Upload finished. Reading {fileName} and building restore preview...");
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
        progress?.Report("Checking free disk space for restore...");
        ArchiveProgressPlan progressPlan = await BuildRestoreProgressPlanAsync(format, archivePath, cancellationToken);
        EnsureEnoughFreeSpaceForRestore(progressPlan.TotalBytes);

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
        UpdateRestoreProgress("Analyzing archive before restore...", null, null, null);

        string restoreId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        string workPath = Path.Combine(InstallStateConstants.BackupRestoreWorkRootPath, restoreId);
        string extractPath = Path.Combine(workPath, "extract");
        Directory.CreateDirectory(extractPath);

        try
        {
            ArchiveProgressPlan progressPlan = await BuildRestoreProgressPlanAsync(
                preview.Format,
                preview.ArchivePath,
                cancellationToken);
            EnsureEnoughFreeSpaceForRestore(progressPlan.TotalBytes);

            UpdateRestoreProgress("Stopping asa.service before restore...", 0D, 0, progressPlan.TotalBytes);
            await StopAsaUntilSafeAsync(cancellationToken, requireServerDirectory: false);

            if (preview.Format == ZipFormat)
            {
                UpdateRestoreProgress("Extracting zip archive into restore workspace...", 0D, 0, progressPlan.TotalBytes);
                string unzipPath = RequireTool(UnzipToolPaths, "unzip");
                await RunRestoreProcessWithProgressAsync(
                    unzipPath,
                    ["-o", preview.ArchivePath, "-d", extractPath],
                    progressPlan,
                    ParseUnzipRestoreOutputPath,
                    "Restoring zip archive",
                    cancellationToken);
            }
            else
            {
                UpdateRestoreProgress("Extracting tar.gz archive into restore workspace...", 0D, 0, progressPlan.TotalBytes);
                string tarPath = RequireTool(TarToolPaths, "tar");
                await RunRestoreProcessWithProgressAsync(
                    tarPath,
                    ["--no-same-owner", "--no-same-permissions", "-xvzf", preview.ArchivePath, "-C", extractPath],
                    progressPlan,
                    ParseTarArchiveOutputPath,
                    "Restoring tar.gz archive",
                    cancellationToken);
            }

            UpdateRestoreProgress("Resolving restored server folder...", 100D, progressPlan.TotalBytes, progressPlan.TotalBytes);
            string sourcePath = ResolveRestoredServerSourcePath(extractPath);
            Directory.CreateDirectory(InstallStateConstants.BackupRootPath);

            string previousServerBackupPath = Path.Combine(
                InstallStateConstants.BackupRootPath,
                $"pre-restore-server-{restoreId}");

            if (Directory.Exists(InstallStateConstants.ServerRootPath))
            {
                UpdateRestoreProgress("Moving current /opt/asa/server into backup...", 100D, progressPlan.TotalBytes, progressPlan.TotalBytes);
                Directory.Move(InstallStateConstants.ServerRootPath, previousServerBackupPath);
            }

            UpdateRestoreProgress("Placing restored files into /opt/asa/server...", 100D, progressPlan.TotalBytes, progressPlan.TotalBytes);
            Directory.Move(sourcePath, InstallStateConstants.ServerRootPath);
            UpdateRestoreProgress("Restore completed. asa.service was left stopped.", 100D, progressPlan.TotalBytes, progressPlan.TotalBytes);
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
        ExportProgressCurrentBytes = 0;
        ExportProgressTotalBytes = null;
        NotifyChanged();
    }

    private void StartTarGzExport(string message)
    {
        IsCreatingZip = false;
        IsCreatingTarGz = true;
        ExportProgressText = message;
        ExportProgressPercent = 0D;
        ExportProgressCurrentBytes = 0;
        ExportProgressTotalBytes = null;
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

    private void UpdateExportProgress(string message, double? percent, long? currentBytes, long? totalBytes)
    {
        ExportProgressText = message;
        ExportProgressPercent = percent is null ? null : Math.Clamp(percent.Value, 0D, 100D);
        ExportProgressCurrentBytes = currentBytes;
        ExportProgressTotalBytes = totalBytes;
        NotifyChanged();
    }

    private void FinishExport(string message)
    {
        IsCreatingZip = false;
        IsCreatingTarGz = false;
        ExportProgressText = message;
        ExportProgressPercent = null;
        ExportProgressCurrentBytes = null;
        ExportProgressTotalBytes = null;
        RefreshToolState();
        NotifyChanged();
        _toastService.ShowSuccess(message, "Backup");
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
        ExportProgressCurrentBytes = null;
        ExportProgressTotalBytes = null;
        RefreshToolState();
        NotifyChanged();
    }

    private void StartRestoreUpload(string fileName, string message)
    {
        RestoreSelectedFileName = fileName;
        RestorePreview = null;
        IsUploadingRestore = true;
        IsPreparingRestorePreview = false;
        IsRestoring = false;
        RestoreProgressText = message;
        RestoreProgressPercent = 0D;
        RestoreProgressCurrentBytes = 0;
        RestoreProgressTotalBytes = null;
        NotifyChanged();
    }

    private void UpdateRestoreProgress(string message)
    {
        RestoreProgressText = message;
        RestoreProgressPercent = null;
        RestoreProgressCurrentBytes = null;
        RestoreProgressTotalBytes = null;
        NotifyChanged();
    }

    private void SetRestorePreview(BackupImportPreview preview, string message)
    {
        RestorePreview = preview;
        IsUploadingRestore = false;
        IsPreparingRestorePreview = false;
        RestoreProgressText = message;
        RestoreProgressPercent = null;
        RestoreProgressCurrentBytes = null;
        RestoreProgressTotalBytes = null;
        _validatedRestoreArchive = BuildArchiveFingerprint(preview.ArchivePath);
        LoadArchives();
        NotifyChanged();
        _toastService.ShowSuccess("Restore capabilities validated.", "Restore");
    }

    private void StartRestore(string message)
    {
        IsUploadingRestore = false;
        IsPreparingRestorePreview = false;
        IsRestoring = true;
        RestoreProgressText = message;
        RestoreProgressPercent = 0D;
        RestoreProgressCurrentBytes = 0;
        RestoreProgressTotalBytes = null;
        NotifyChanged();
    }

    private void FinishRestore(string message)
    {
        IsRestoring = false;
        RestoreProgressText = message;
        RestoreProgressPercent = null;
        RestoreProgressCurrentBytes = null;
        RestoreProgressTotalBytes = null;
        RefreshToolState();
        LoadArchives();
        NotifyChanged();
        _toastService.ShowSuccess("Restore finished.", "Restore");
    }

    private void FailRestore(string? message = null)
    {
        IsUploadingRestore = false;
        IsPreparingRestorePreview = false;
        IsRestoring = false;
        if (!string.IsNullOrWhiteSpace(message))
        {
            RestoreProgressText = message;
        }

        RestoreProgressPercent = null;
        RestoreProgressCurrentBytes = null;
        RestoreProgressTotalBytes = null;
        NotifyChanged();
    }

    private void ClearValidatedRestorePreview()
    {
        RestorePreview = null;
        RestoreSelectedFileName = null;
        RestoreProgressText = null;
        RestoreProgressPercent = null;
        RestoreProgressCurrentBytes = null;
        RestoreProgressTotalBytes = null;
        _validatedRestoreArchive = null;
    }

    private void StartRestoreValidation(string message)
    {
        IsUploadingRestore = false;
        IsPreparingRestorePreview = true;
        IsRestoring = false;
        RestoreProgressText = message;
        RestoreProgressPercent = null;
        RestoreProgressCurrentBytes = null;
        RestoreProgressTotalBytes = null;
        NotifyChanged();
    }

    private void UpdateRestoreProgress(string message, double? percent, long? currentBytes, long? totalBytes)
    {
        RestoreProgressText = message;
        RestoreProgressPercent = percent is null ? null : Math.Clamp(percent.Value, 0D, 100D);
        RestoreProgressCurrentBytes = currentBytes;
        RestoreProgressTotalBytes = totalBytes;
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

        await using AsyncServiceScope scope = _serviceScopeFactory.CreateAsyncScope();
        InstallStateService installStateService = scope.ServiceProvider.GetRequiredService<InstallStateService>();

        Models.Asa.AsaServiceStatus status = await installStateService.GetAsaServiceStatusAsync(cancellationToken);
        if (status.IsUnavailable)
        {
            throw new InvalidOperationException("asa service status is unavailable. Backup cannot verify the server is stopped.");
        }

        if (status.CanStop)
        {
            await installStateService.StopAsaServiceAsync(cancellationToken);
        }
        else if (!status.IsStopped && !status.IsFailed)
        {
            throw new InvalidOperationException($"asa cannot be stopped while it is {status.DisplayText.ToLowerInvariant()}.");
        }

        DateTimeOffset stopDeadline = DateTimeOffset.UtcNow.Add(StopTimeout);
        do
        {
            status = await installStateService.GetAsaServiceStatusAsync(cancellationToken);
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

    private static void DeleteExistingArchives(string format)
    {
        string searchPattern = format switch
        {
            ZipFormat => "asa-server-*.zip",
            TarGzFormat => "asa-server-*.tar.gz",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(searchPattern) || !Directory.Exists(InstallStateConstants.BackupRootPath))
        {
            return;
        }

        foreach (string filePath in Directory.EnumerateFiles(
                     InstallStateConstants.BackupRootPath,
                     searchPattern,
                     SearchOption.TopDirectoryOnly))
        {
            File.Delete(filePath);
        }
    }

    private static ArchiveProgressPlan BuildArchiveProgressPlan(string sourceRootPath, string archiveRootName)
    {
        Dictionary<string, long> fileSizes = new(StringComparer.Ordinal);
        long totalBytes = 0;
        EnumerationOptions enumerationOptions = new()
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        foreach (string filePath in Directory.EnumerateFiles(sourceRootPath, "*", enumerationOptions))
        {
            try
            {
                string relativePath = Path.GetRelativePath(sourceRootPath, filePath).Replace('\\', '/');
                string archiveEntryPath = $"{archiveRootName}/{relativePath}";
                long sizeBytes = new FileInfo(filePath).Length;
                fileSizes[archiveEntryPath] = sizeBytes;
                totalBytes += sizeBytes;
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
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

    private void EnsureEnoughFreeSpaceForBackup(string format, long sourceBytes)
    {
        long requiredBytes = checked(sourceBytes + ArchiveSpaceOverheadBytes);
        long availableBytes = GetAvailableRootFreeBytes() + GetExistingArchiveBytes(format);
        if (availableBytes >= requiredBytes)
        {
            return;
        }

        ThrowNotEnoughSpace(
            "Backup",
            $"Not enough disk space to create this {format} backup. Need about {FormatBytes(requiredBytes)}, have {FormatBytes(availableBytes)} free after replacing the current {format} archive.");
    }

    private void EnsureEnoughFreeSpaceForRestore(long restoreBytes)
    {
        long requiredBytes = checked(restoreBytes + ArchiveSpaceOverheadBytes);
        long availableBytes = GetAvailableRootFreeBytes();
        if (availableBytes >= requiredBytes)
        {
            return;
        }

        ThrowNotEnoughSpace(
            "Restore",
            $"Not enough disk space to restore this backup. Need about {FormatBytes(requiredBytes)} free to extract it, have {FormatBytes(availableBytes)} free.");
    }

    private void ThrowNotEnoughSpace(string tag, string message)
    {
        _toastService.ShowError(message, tag);
        throw new InvalidOperationException(message);
    }

    private static long GetAvailableRootFreeBytes()
    {
        DriveInfo rootDrive = new("/");
        return rootDrive.AvailableFreeSpace;
    }

    private static long GetExistingArchiveBytes(string format)
    {
        if (!Directory.Exists(InstallStateConstants.BackupRootPath))
        {
            return 0;
        }

        string searchPattern = format switch
        {
            ZipFormat => "asa-server-*.zip",
            TarGzFormat => "asa-server-*.tar.gz",
            _ => string.Empty
        };

        if (string.IsNullOrWhiteSpace(searchPattern))
        {
            return 0;
        }

        long totalBytes = 0;
        foreach (string filePath in Directory.EnumerateFiles(
                     InstallStateConstants.BackupRootPath,
                     searchPattern,
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                totalBytes += new FileInfo(filePath).Length;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return totalBytes;
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = Math.Abs(bytes);
        int unitIndex = 0;
        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{Math.Sign(bytes) * value:0.##} {units[unitIndex]}";
    }

    private bool IsArchiveValidatedForRestore(BackupArchiveInfo archive)
    {
        ArchiveFingerprint? currentArchive = BuildArchiveFingerprint(archive.FilePath);
        return currentArchive is not null &&
               _validatedRestoreArchive is not null &&
               currentArchive == _validatedRestoreArchive;
    }

    private static ArchiveFingerprint? BuildArchiveFingerprint(string archivePath)
    {
        FileInfo fileInfo = new(archivePath);
        return fileInfo.Exists
            ? new ArchiveFingerprint(fileInfo.FullName, fileInfo.Length, fileInfo.LastWriteTimeUtc)
            : null;
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
                percent,
                processedBytes,
                progressPlan.TotalBytes);
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            string error = await standardErrorTask;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Archive command failed." : error.Trim());
        }
    }

    private async Task RunRestoreProcessWithProgressAsync(
        string fileName,
        IReadOnlyList<string> arguments,
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
                CreateNoWindow = true
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

            UpdateRestoreProgress(
                $"{operationLabel}... {processedFiles}/{progressPlan.TotalFiles} files",
                percent,
                processedBytes,
                progressPlan.TotalBytes);
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

    private async Task CopyStreamWithProgressAsync(
        Stream source,
        Stream destination,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[1024 * 128];
        long uploadedBytes = 0;

        while (true)
        {
            int bytesRead = await source.ReadAsync(buffer, cancellationToken);
            if (bytesRead <= 0)
            {
                break;
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            uploadedBytes += bytesRead;

            if (totalBytes > 0)
            {
                double percent = Math.Clamp((double)uploadedBytes / totalBytes * 100D, 0D, 100D);
                UpdateRestoreProgress(
                    $"Uploading {RestoreSelectedFileName}...",
                    percent,
                    uploadedBytes,
                    totalBytes);
            }
        }
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

    private static string? ParseUnzipRestoreOutputPath(string line)
    {
        string trimmedLine = line.Trim();
        string[] prefixes = ["inflating: ", "extracting: ", "  inflating: ", "  extracting: "];

        foreach (string prefix in prefixes)
        {
            if (trimmedLine.StartsWith(prefix.TrimStart(), StringComparison.Ordinal))
            {
                string value = trimmedLine[prefix.TrimStart().Length..];
                return NormalizeArchiveOutputPath(value);
            }
        }

        return null;
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

    private static async Task<ArchiveProgressPlan> BuildRestoreProgressPlanAsync(
        string format,
        string archivePath,
        CancellationToken cancellationToken)
    {
        return format switch
        {
            ZipFormat => BuildZipRestoreProgressPlan(archivePath),
            TarGzFormat => await BuildTarRestoreProgressPlanAsync(archivePath, cancellationToken),
            _ => throw new InvalidOperationException("Unsupported backup format.")
        };
    }

    private static ArchiveProgressPlan BuildZipRestoreProgressPlan(string archivePath)
    {
        Dictionary<string, long> fileSizes = new(StringComparer.Ordinal);
        long totalBytes = 0;

        using ZipArchive archive = ZipFile.OpenRead(archivePath);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string entryPath = NormalizeArchiveOutputPath(entry.FullName) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entryPath) || entry.FullName.EndsWith("/", StringComparison.Ordinal))
            {
                continue;
            }

            fileSizes[entryPath] = entry.Length;
            totalBytes += entry.Length;
        }

        return new ArchiveProgressPlan(fileSizes, totalBytes, fileSizes.Count);
    }

    private static async Task<ArchiveProgressPlan> BuildTarRestoreProgressPlanAsync(string archivePath, CancellationToken cancellationToken)
    {
        string tarPath = RequireTool(TarToolPaths, "tar");
        string output = await RunProcessForOutputAsync(tarPath, ["-tvzf", archivePath], cancellationToken);
        Dictionary<string, long> fileSizes = new(StringComparer.Ordinal);
        long totalBytes = 0;

        foreach (string line in output.Replace("\r\n", "\n", StringComparison.Ordinal)
                     .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!TryParseTarListLine(line, out string? entryPath, out long sizeBytes) ||
                string.IsNullOrWhiteSpace(entryPath))
            {
                continue;
            }

            fileSizes[entryPath] = sizeBytes;
            totalBytes += sizeBytes;
        }

        return new ArchiveProgressPlan(fileSizes, totalBytes, fileSizes.Count);
    }

    private static bool TryParseTarListLine(string line, out string? entryPath, out long sizeBytes)
    {
        entryPath = null;
        sizeBytes = 0;

        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 6 || parts[0].StartsWith('d'))
        {
            return false;
        }

        if (!long.TryParse(parts[2], out sizeBytes))
        {
            return false;
        }

        string rawPath = string.Join(' ', parts.Skip(5));
        int symlinkIndex = rawPath.IndexOf(" -> ", StringComparison.Ordinal);
        if (symlinkIndex >= 0)
        {
            rawPath = rawPath[..symlinkIndex];
        }

        entryPath = NormalizeArchiveOutputPath(rawPath);
        return !string.IsNullOrWhiteSpace(entryPath);
    }

    private sealed record ArchiveProgressPlan(
        IReadOnlyDictionary<string, long> FileSizes,
        long TotalBytes,
        int TotalFiles);

    private sealed record ArchiveFingerprint(
        string FullPath,
        long SizeBytes,
        DateTimeOffset LastWriteTimeUtc);
}
