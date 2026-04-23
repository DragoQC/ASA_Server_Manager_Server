namespace asa_server_node_api.Contracts.Api.Admin;

public sealed record InviteRemoteServerResponse(
    bool Accepted,
    string? Message,
    string? Path);
