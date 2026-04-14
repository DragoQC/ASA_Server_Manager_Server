namespace AsaServerManager.Web.Models.Rcon;

internal sealed record ResolvedRconSettings(
    RconStatus Status,
    string Password);
