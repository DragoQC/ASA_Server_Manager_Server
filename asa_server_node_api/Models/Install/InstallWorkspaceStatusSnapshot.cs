namespace asa_server_node_api.Models.Install;

public sealed record InstallWorkspaceStatusSnapshot(
    InstallToolState Proton,
    InstallToolState Steam,
    InstallFileStatusSnapshot StartScript,
    InstallFileStatusSnapshot ServiceFile,
    DateTimeOffset CheckedAtUtc);
