namespace asa_server_node_api.Models.Auth;

public sealed record StoredApiKey(
    string Type,
    string ApiKey,
    DateTimeOffset ModifiedAtUtc);
