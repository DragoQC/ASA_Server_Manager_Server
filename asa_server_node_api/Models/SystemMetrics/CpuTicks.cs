namespace asa_server_node_api.Models.SystemMetrics;

internal sealed record CpuTicks(
    long TotalTicks,
    long IdleTicks);
