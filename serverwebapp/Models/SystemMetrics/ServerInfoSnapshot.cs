namespace AsaServerManager.Web.Models.SystemMetrics;

public sealed record ServerInfoSnapshot(
    string ServerName,
    string MapName,
    int GamePort,
    int MaxPlayers,
    IReadOnlyList<string> ModIds,
    DateTimeOffset CheckedAtUtc);
