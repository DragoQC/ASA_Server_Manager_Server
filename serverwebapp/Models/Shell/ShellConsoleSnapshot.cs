namespace AsaServerManager.Web.Models.Shell;

public sealed record ShellConsoleSnapshot(
    string RequestedWorkingDirectory,
    string WorkingDirectory,
    bool WorkingDirectoryExists,
    bool IsRunning,
    bool IsBusy,
    string Transcript,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset UpdatedAtUtc);
