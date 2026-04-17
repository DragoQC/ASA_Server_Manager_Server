namespace AsaServerManager.Web.Contracts.Api.Admin;

public sealed record UploadFileResult(
    bool Success,
    string Message,
    string? Path);
