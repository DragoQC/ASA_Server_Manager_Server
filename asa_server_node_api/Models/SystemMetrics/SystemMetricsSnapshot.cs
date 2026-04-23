namespace asa_server_node_api.Models.SystemMetrics;

public sealed record SystemMetricsSnapshot(
    string CpuTotal,
    string CpuUsage,
    string DiskTotal,
    string DiskUsed,
    string DownloadSpeed,
    string UploadSpeed,
    string RamTotal,
    string RamUsed,
    DateTimeOffset CheckedAtUtc);
