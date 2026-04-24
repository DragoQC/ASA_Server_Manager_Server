namespace asa_server_node_api.Contracts.Api.Admin;

public sealed record PatchServerConfigRequest(
    string? ServerName,
    string? MapName,
    int? MaxPlayers,
    int? GamePort,
    IReadOnlyList<string>? ModIds,
    string? ClusterId);
