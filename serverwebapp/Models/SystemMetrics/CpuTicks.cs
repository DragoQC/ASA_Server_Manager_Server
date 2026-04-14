namespace AsaServerManager.Web.Models.SystemMetrics;

internal sealed record CpuTicks(
    long TotalTicks,
    long IdleTicks);
