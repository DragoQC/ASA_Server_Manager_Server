namespace asa_server_node_api.Models.Admin;

public sealed record AdminHostMetricsSnapshot(
    string CpuUsage,
    string RamUsage,
    string RamUsed,
    string DiskUsage,
    string DiskUsed,
    DateTimeOffset CheckedAtUtc)
{
    public static AdminHostMetricsSnapshot Default() =>
        new(
            CpuUsage: "0%",
            RamUsage: "0%",
            RamUsed: "0 B",
            DiskUsage: "0%",
            DiskUsed: "0 B",
            CheckedAtUtc: DateTimeOffset.UtcNow);
}
