namespace asa_server_node_api.Models.Actions;

public sealed record ActionMapping(
    string CommandText,
    string ActionType,
    string ActionValue,
    string Description);
