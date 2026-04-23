namespace asa_server_node_api.Models.Install;

public sealed record ProtonReleaseState(
    string CurrentVersion,
    string LatestVersion,
    string DownloadUrl,
    bool UpdateAvailable);
