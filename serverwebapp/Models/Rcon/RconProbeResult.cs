namespace AsaServerManager.Web.Models.Rcon;

public sealed record RconProbeResult(
    bool IsConnected,
    string Host,
    int Port,
    string StateLabel,
    string Message);
