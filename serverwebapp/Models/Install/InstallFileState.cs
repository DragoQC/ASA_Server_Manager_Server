namespace AsaServerManager.Web.Models.Install;

public sealed record InstallFileState(
    string Title,
    string Description,
    string Status,
    string StateLabel,
    string FilePath,
    string Content);
