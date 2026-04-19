namespace AsaServerManager.Web.Contracts.Api.Admin;

public sealed record InviteRemoteServerRequest(
    string VpnAddress,
    string ClusterId,
    string ServerEndpoint,
    string Dns,
    string AllowedIps,
    string RemoteApiKey,
    string ServerPublicKey,
    string ClientPrivateKey,
    string Wg0Config,
    string? PresharedKey);
