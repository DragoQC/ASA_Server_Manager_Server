namespace asa_server_node_api.Models.Install;

public sealed record InstallFileStatusSnapshot(
    string Title,
    string Description,
    string Status,
    string StateLabel,
    string FilePath);
