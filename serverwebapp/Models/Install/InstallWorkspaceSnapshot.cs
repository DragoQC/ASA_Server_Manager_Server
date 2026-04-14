namespace AsaServerManager.Web.Models.Install;

public sealed record InstallWorkspaceSnapshot(
    InstallToolState Proton,
    InstallToolState Steam,
    InstallFileState StartScript,
    InstallFileState ServiceFile,
    DateTimeOffset CheckedAtUtc);
