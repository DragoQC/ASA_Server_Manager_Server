This must be able to also take in the custom extra args input as string or list of strings.
Also why the mod list is readonly hmmm cause like if its a patch normaly i dont know...
Query port is also missing but i think we dont use it so its totaly fine i guess

namespace asa_server_node_api.Contracts.Api.Admin;

public sealed record PatchServerConfigRequest(
    string? ServerName,
    string? MapName,
    int? MaxPlayers,
    int? GamePort,
    IReadOnlyList<string>? ModIds,
    string? ClusterId);
