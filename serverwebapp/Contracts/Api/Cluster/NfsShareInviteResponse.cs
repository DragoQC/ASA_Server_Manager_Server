namespace AsaServerManager.Web.Contracts.Api.Cluster;

public sealed record NfsShareInviteResponse(
    string SharePath,
    string MountPath,
    string ClientConfig);
