namespace AsaServerManager.Web.Models.Auth;

public sealed record StoredApiKey(
    string Type,
    string ApiKey,
    DateTimeOffset ModifiedAtUtc);
