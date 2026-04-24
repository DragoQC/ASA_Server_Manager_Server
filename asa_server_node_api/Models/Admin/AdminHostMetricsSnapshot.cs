namespace asa_server_node_api.Models.Admin;

public sealed record AdminHostMetricsSnapshot(
    string CpuUsage,
    string RamPercentage,
    string RamTotal,
    string DiskTotal,
    string DiskUsed,
    DateTimeOffset CheckedAtUtc)
{
    public static AdminHostMetricsSnapshot Default() =>
        new(
            CpuUsage: "0%",
            RamPercentage: "0%",
            RamTotal: "0 B",
            DiskTotal: "0 B",
            DiskUsed: "0 B",
            CheckedAtUtc: DateTimeOffset.UtcNow);
}
