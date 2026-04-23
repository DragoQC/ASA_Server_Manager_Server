namespace asa_server_node_api.Models.SystemMetrics;

internal sealed record Sample(
    DateTimeOffset TimestampUtc,
    CpuTicks CpuTicks,
    long DiskTransferredBytes,
    long ReceivedBytes,
    long TransmittedBytes);
