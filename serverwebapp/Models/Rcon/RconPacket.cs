namespace AsaServerManager.Web.Models.Rcon;

internal sealed record RconPacket(
    int Id,
    int Type,
    string Body);
