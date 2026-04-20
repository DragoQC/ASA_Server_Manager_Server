using System.Globalization;
using AsaServerManager.Web.Models.SystemMetrics;

namespace AsaServerManager.Web.Services;

public sealed class SystemMetricsService(ServerConfigService serverConfigService)
{
    private readonly ServerConfigService _serverConfigService = serverConfigService;
    private Sample? _previousSample;

    public async Task<ServerInfoSnapshot> LoadServerInfoAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Models.ServerConfig.ServerConfigSettings settings = await _serverConfigService.LoadAsync(cancellationToken);

        ServerInfoSnapshot snapshot = new(
            MapName: settings.MapName,
            MaxPlayers: settings.MaxPlayers,
            CheckedAtUtc: DateTimeOffset.UtcNow);

        return snapshot;
    }

    public Task<SystemMetricsSnapshot> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Sample currentSample = new(
            DateTimeOffset.UtcNow,
            ReadCpuTicks(),
            ReadDiskTransferredBytes(),
            ReadReceivedBytes(),
            ReadTransmittedBytes());

        double elapsedSeconds = _previousSample is null
            ? 0
            : Math.Max(0.001D, (currentSample.TimestampUtc - _previousSample.TimestampUtc).TotalSeconds);

        double cpuUsagePercentage = _previousSample is null
            ? 0
            : CalculateCpuUsagePercentage(_previousSample.CpuTicks, currentSample.CpuTicks);

        double downloadBytesPerSecond = _previousSample is null
            ? 0
            : Math.Max(0, currentSample.ReceivedBytes - _previousSample.ReceivedBytes) / elapsedSeconds;

        double uploadBytesPerSecond = _previousSample is null
            ? 0
            : Math.Max(0, currentSample.TransmittedBytes - _previousSample.TransmittedBytes) / elapsedSeconds;

        _previousSample = currentSample;

        (long totalRamBytes, long usedRamBytes) = ReadMemoryBytes();
        (long totalDiskBytes, long usedDiskBytes) = ReadRootDiskBytes();

        SystemMetricsSnapshot snapshot = new(
            $"{Environment.ProcessorCount} logical",
            $"{cpuUsagePercentage:0.#}%",
            FormatBytes(totalDiskBytes),
            FormatBytes(usedDiskBytes),
            $"↓ {FormatBytes((long)downloadBytesPerSecond)}/s",
            $"↑ {FormatBytes((long)uploadBytesPerSecond)}/s",
            FormatBytes(totalRamBytes),
            FormatBytes(usedRamBytes),
            currentSample.TimestampUtc);

        return Task.FromResult(snapshot);
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

    private static long ReadDiskTransferredBytes()
    {
        if (!File.Exists("/proc/diskstats"))
        {
            return 0;
        }

        long totalBytes = 0;

        foreach (string line in File.ReadLines("/proc/diskstats"))
        {
            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 10)
            {
                continue;
            }

            string deviceName = parts[2];
            if (!IsPhysicalDiskName(deviceName))
            {
                continue;
            }

            if (!long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out long readSectors))
            {
                continue;
            }

            if (!long.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out long writtenSectors))
            {
                continue;
            }

            totalBytes += (readSectors + writtenSectors) * 512;
        }

        return totalBytes;
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

    private static bool IsPhysicalDiskName(string deviceName)
    {
        if (deviceName.StartsWith("sd", StringComparison.Ordinal) && deviceName.Length == 3)
        {
            return true;
        }

        if (deviceName.StartsWith("vd", StringComparison.Ordinal) && deviceName.Length == 3)
        {
            return true;
        }

        if (deviceName.StartsWith("xvd", StringComparison.Ordinal) && deviceName.Length == 4)
        {
            return true;
        }

        if (deviceName.StartsWith("nvme", StringComparison.Ordinal) &&
            !deviceName.Contains('p', StringComparison.Ordinal) &&
            deviceName.Contains('n', StringComparison.Ordinal))
        {
            return true;
        }

        if (deviceName.StartsWith("mmcblk", StringComparison.Ordinal) &&
            !deviceName.Contains('p', StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static long ReadReceivedBytes()
    {
        return ReadNetworkBytes("rx_bytes");
    }

    private static long ReadTransmittedBytes()
    {
        return ReadNetworkBytes("tx_bytes");
    }

    private static long ReadNetworkBytes(string statFileName)
    {
        const string networkRoot = "/sys/class/net";
        if (!Directory.Exists(networkRoot))
        {
            return 0;
        }

        long totalBytes = 0;

        foreach (string interfaceDirectoryPath in Directory.GetDirectories(networkRoot, "*", SearchOption.TopDirectoryOnly))
        {
            string interfaceName = Path.GetFileName(interfaceDirectoryPath);
            if (string.Equals(interfaceName, "lo", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string statFilePath = Path.Combine(interfaceDirectoryPath, "statistics", statFileName);
            if (!File.Exists(statFilePath))
            {
                continue;
            }

            string content = File.ReadAllText(statFilePath).Trim();
            if (long.TryParse(content, NumberStyles.Integer, CultureInfo.InvariantCulture, out long bytes))
            {
                totalBytes += bytes;
            }
        }

        return totalBytes;
    }

    private static string FormatBytes(long value)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        double size = Math.Max(0, value);
        int unitIndex = 0;

        while (size >= 1024 && unitIndex < units.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        string format = size >= 100 || unitIndex == 0 ? "0" : "0.0";
        return $"{size.ToString(format, CultureInfo.InvariantCulture)} {units[unitIndex]}";
    }
}
