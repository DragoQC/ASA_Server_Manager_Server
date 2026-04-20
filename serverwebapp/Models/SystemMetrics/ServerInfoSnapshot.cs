namespace AsaServerManager.Web.Models.SystemMetrics;

public sealed record ServerInfoSnapshot(
    string MapName,
    int MaxPlayers,
    DateTimeOffset CheckedAtUtc);
