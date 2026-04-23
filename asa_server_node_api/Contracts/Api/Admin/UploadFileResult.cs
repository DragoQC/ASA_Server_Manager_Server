namespace asa_server_node_api.Contracts.Api.Admin;

public sealed record UploadFileResult(
    bool Success,
    string Message,
    string? Path);
