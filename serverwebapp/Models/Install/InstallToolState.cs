namespace AsaServerManager.Web.Models.Install;

public sealed record InstallToolState(
    string Title,
    string Description,
    string Status,
    string StateLabel,
    string? VersionLabel,
    string? LatestVersionLabel,
    string InstallPath,
    bool CanUpdate,
    bool CanRevert);
