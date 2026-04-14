namespace AsaServerManager.Web.Models.GameConfig;

public sealed record GameConfigFileState(
    string Title,
    string Description,
    string FilePath,
    string StateLabel,
    bool CanOpen,
    bool Exists);
