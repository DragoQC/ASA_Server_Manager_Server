namespace asa_server_node_api.Models.Players;

public sealed record PlayerCountSnapshot(
    int CurrentPlayers,
    int MaxPlayers,
    string StatusLabel,
    string Message,
    IReadOnlyList<OnlinePlayerSnapshot> Players,
    DateTimeOffset UpdatedAtUtc)
{
    public static PlayerCountSnapshot Default(int maxPlayers = 20) =>
        new(
            CurrentPlayers: 0,
            MaxPlayers: maxPlayers,
            StatusLabel: "Waiting",
            Message: "Waiting for first player poll.",
            Players: [],
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}
