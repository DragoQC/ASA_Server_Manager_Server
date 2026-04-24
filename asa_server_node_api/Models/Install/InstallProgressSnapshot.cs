namespace asa_server_node_api.Models.Install;

public sealed record InstallProgressSnapshot(
    string Operation,
    string Step,
    string State,
    string Message,
    DateTimeOffset UpdatedAtUtc)
{
    public static InstallProgressSnapshot Idle() =>
        new(
            Operation: "idle",
            Step: "idle",
            State: "Idle",
            Message: "No install operation is running.",
            UpdatedAtUtc: DateTimeOffset.UtcNow);
}
