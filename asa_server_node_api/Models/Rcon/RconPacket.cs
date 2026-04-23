namespace asa_server_node_api.Models.Rcon;

internal sealed record RconPacket(
    int Id,
    int Type,
    string Body);
