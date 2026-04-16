namespace AsaServerManager.Web.Models.Rcon;

public sealed record RconSettings(
    int Port,
    string Password,
    bool IsEnabled)
{
    public static RconSettings Default(int port = 27020) =>
        new(
            Port: port,
            Password: string.Empty,
            IsEnabled: false);
}
