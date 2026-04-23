namespace asa_server_node_api.Contracts.Api.Cluster;

public sealed record SmbShareInviteResponse(
    string SharePath,
    string MountPath,
    string ClientConfig);
