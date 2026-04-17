namespace AsaServerManager.Web.Contracts.Api.Admin;

public sealed record InviteRemoteServerRequest(
    string VpnAddress,
    string ServerEndpoint,
    string Dns,
    string AllowedIps,
    string ServerPublicKey,
    string ClientPrivateKey,
    string? PresharedKey);
