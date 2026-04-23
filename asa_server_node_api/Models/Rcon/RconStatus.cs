namespace asa_server_node_api.Models.Rcon;

public sealed record RconStatus(
    bool HasEnabledKey,
    bool HasPort,
    bool HasPasswordKey,
    bool HasPassword,
    string StateLabel,
    string Message)
{
    public bool CanExecute(RconSettings settings) =>
        settings.IsEnabled && HasPort && HasPassword;

    public static RconStatus Unknown() =>
        new(
            false,
            false,
            false,
            false,
            "Missing",
            "Checking RCON...");
}
