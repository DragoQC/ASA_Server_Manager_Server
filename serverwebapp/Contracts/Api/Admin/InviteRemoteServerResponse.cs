namespace AsaServerManager.Web.Contracts.Api.Admin;

public sealed record InviteRemoteServerResponse(
    bool Accepted,
    string? Message,
    string? Path);
