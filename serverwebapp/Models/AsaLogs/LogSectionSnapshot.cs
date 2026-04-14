namespace AsaServerManager.Web.Models.AsaLogs;

public sealed record LogSectionSnapshot(
    string Title,
    string Description,
    string Content,
    bool IsAvailable);
