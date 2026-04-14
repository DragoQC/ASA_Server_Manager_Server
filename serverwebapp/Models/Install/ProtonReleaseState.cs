namespace AsaServerManager.Web.Models.Install;

public sealed record ProtonReleaseState(
    string CurrentVersion,
    string LatestVersion,
    string DownloadUrl,
    bool UpdateAvailable);
