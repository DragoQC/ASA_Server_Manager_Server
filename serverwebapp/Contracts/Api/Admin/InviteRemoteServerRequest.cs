namespace AsaServerManager.Web.Contracts.Api.Admin;

public sealed record InviteRemoteServerRequest(
    string ClusterId,
    string VpnAddress,
    string ServerEndpoint,
    string AllowedIps,
    string RemoteApiKey,
    string ServerPublicKey,
    string ClientPrivateKey,
    string Wg0Config,
    string? PresharedKey);
