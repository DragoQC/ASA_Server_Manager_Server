namespace asa_server_node_api.Models.Asa;

public sealed record AsaServiceStatus(
    string ActiveState,
    string SubState,
    string Result,
    string UnitFileState,
    string DisplayText,
    bool CanStart,
    bool CanStop,
    DateTimeOffset? ActiveSinceUtc,
    string UptimeText)
{
    public bool IsRunning => string.Equals(ActiveState, "active", StringComparison.Ordinal);

    public bool IsStarting => string.Equals(ActiveState, "activating", StringComparison.Ordinal);

    public bool IsStopping => string.Equals(ActiveState, "deactivating", StringComparison.Ordinal);

    public bool IsStopped => string.Equals(ActiveState, "inactive", StringComparison.Ordinal);

    public bool IsFailed => string.Equals(ActiveState, "failed", StringComparison.Ordinal);

    public bool IsUnavailable => string.Equals(ActiveState, "unknown", StringComparison.Ordinal);

    public bool IsUpOrStarting => IsRunning || IsStarting;

    public bool ShouldPromptForRestart => IsUpOrStarting;

    public static AsaServiceStatus Unknown(string displayText = "Unknown")
    {
        return new AsaServiceStatus("unknown", "unknown", "unknown", "unknown", displayText, false, false, null, "Unavailable");
    }
}
