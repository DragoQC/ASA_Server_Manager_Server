namespace asa_server_node_api.Models.GameConfig;

public sealed record GameConfigFileState(
    string Title,
    string Description,
    string FilePath,
    string StateLabel,
    bool CanOpen,
    bool Exists);
