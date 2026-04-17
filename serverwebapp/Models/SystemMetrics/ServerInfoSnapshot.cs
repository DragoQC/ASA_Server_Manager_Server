namespace AsaServerManager.Web.Models.SystemMetrics;

public sealed record ServerInfoSnapshot(
    string CpuTotal,
    string RamTotal,
    string DiskTotal,
    DateTimeOffset CheckedAtUtc);
