using System.Globalization;
using asa_server_node_api.Models.Admin;

namespace asa_server_node_api.Services;

public sealed class AdminHostMetricsMonitorService(ILogger<AdminHostMetricsMonitorService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ILogger<AdminHostMetricsMonitorService> _logger = logger;
    private volatile AdminHostMetricsSnapshot _current = AdminHostMetricsSnapshot.Default();
    private Sample? _previousSample;

    public event Action? Changed;

    public AdminHostMetricsSnapshot GetSnapshot() => _current;

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

    private Task RefreshAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            Sample currentSample = new(
                DateTimeOffset.UtcNow,
                ReadCpuTicks());

            double cpuUsagePercentage = _previousSample is null
                ? 0
                : CalculateCpuUsagePercentage(_previousSample.CpuTicks, currentSample.CpuTicks);

            _previousSample = currentSample;

            (long totalRamBytes, long usedRamBytes) = ReadMemoryBytes();
            (long totalDiskBytes, long usedDiskBytes) = ReadRootDiskBytes();
            double ramUsagePercentage = totalRamBytes <= 0
                ? 0
                : Math.Clamp((double)usedRamBytes / totalRamBytes * 100D, 0D, 100D);

            _current = new AdminHostMetricsSnapshot(
                CpuUsage: $"{cpuUsagePercentage:0.#}%",
                RamPercentage: $"{ramUsagePercentage:0.#}%",
                RamTotal: FormatBytes(totalRamBytes),
                DiskTotal: FormatBytes(totalDiskBytes),
                DiskUsed: FormatBytes(usedDiskBytes),
                CheckedAtUtc: currentSample.TimestampUtc);

            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh admin host metrics.");
        }

        return Task.CompletedTask;
    }

    private static (long TotalBytes, long UsedBytes) ReadRootDiskBytes()
    {
        DriveInfo rootDrive = new("/");
        if (!rootDrive.IsReady)
        {
            return (0, 0);
        }

        long totalBytes = rootDrive.TotalSize;
        long usedBytes = Math.Max(0, rootDrive.TotalSize - rootDrive.AvailableFreeSpace);
        return (totalBytes, usedBytes);
    }

    private static (long TotalBytes, long UsedBytes) ReadMemoryBytes()
    {
        if (!File.Exists("/proc/meminfo"))
        {
            return (0, 0);
        }

        long totalKilobytes = 0;
        long availableKilobytes = 0;

        foreach (string line in File.ReadLines("/proc/meminfo"))
        {
            if (line.StartsWith("MemTotal:", StringComparison.Ordinal))
            {
                totalKilobytes = ParseMemInfoKilobytes(line);
            }
            else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal))
            {
                availableKilobytes = ParseMemInfoKilobytes(line);
            }
        }

        long totalBytes = totalKilobytes * 1024;
        long usedBytes = Math.Max(0, (totalKilobytes - availableKilobytes) * 1024);
        return (totalBytes, usedBytes);
    }

    private static long ParseMemInfoKilobytes(string line)
    {
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value)
            ? value
            : 0;
    }

    private static CpuTicks ReadCpuTicks()
    {
        if (!File.Exists("/proc/stat"))
        {
            return new CpuTicks(0, 0);
        }

        string? firstLine = File.ReadLines("/proc/stat").FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
        {
            return new CpuTicks(0, 0);
        }

        string[] parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 5 || !string.Equals(parts[0], "cpu", StringComparison.Ordinal))
        {
            return new CpuTicks(0, 0);
        }

        long[] values = new long[Math.Max(0, parts.Length - 1)];

        for (int index = 1; index < parts.Length; index++)
        {
            if (!long.TryParse(parts[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out long value))
            {
                value = 0;
            }

            values[index - 1] = value;
        }

        long idleTicks = values.Length > 3 ? values[3] : 0;
        long iowaitTicks = values.Length > 4 ? values[4] : 0;
        long totalTicks = values.Sum();

        return new CpuTicks(totalTicks, idleTicks + iowaitTicks);
    }

    private static double CalculateCpuUsagePercentage(CpuTicks previousCpuTicks, CpuTicks currentCpuTicks)
    {
        long totalDelta = Math.Max(0, currentCpuTicks.TotalTicks - previousCpuTicks.TotalTicks);
        long idleDelta = Math.Max(0, currentCpuTicks.IdleTicks - previousCpuTicks.IdleTicks);

        if (totalDelta <= 0)
        {
            return 0;
        }

        return Math.Clamp((1D - (double)idleDelta / totalDelta) * 100D, 0D, 100D);
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double value = Math.Max(0, bytes);
        int unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return value >= 100 || unitIndex == 0
            ? $"{value:0} {units[unitIndex]}"
            : $"{value:0.#} {units[unitIndex]}";
    }

    private sealed record CpuTicks(long TotalTicks, long IdleTicks);

    private sealed record Sample(
        DateTimeOffset TimestampUtc,
        CpuTicks CpuTicks);
}
