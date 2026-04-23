namespace asa_server_node_api.Models.AsaLogs;

public sealed record LogSectionSnapshot(
    string Title,
    string Description,
    string Content,
    bool IsAvailable);
