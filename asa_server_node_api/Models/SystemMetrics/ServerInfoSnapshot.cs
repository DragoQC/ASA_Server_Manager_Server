namespace asa_server_node_api.Models.SystemMetrics;

public sealed record ServerInfoSnapshot(
    string ServerName,
    string MapName,
    int GamePort,
    int MaxPlayers,
    int CpuCount,
    IReadOnlyList<string> ModIds,
    DateTimeOffset CheckedAtUtc);
