namespace asa_server_node_api.Models.AsaLogs;

internal sealed record CommandResult(
    int ExitCode,
    string Output);
