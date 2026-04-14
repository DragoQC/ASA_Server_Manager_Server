namespace AsaServerManager.Web.Models.AsaLogs;

internal sealed record CommandResult(
    int ExitCode,
    string Output);
