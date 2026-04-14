namespace AsaServerManager.Web.Models.Actions;

public sealed record ActionMapping(
    string CommandText,
    string ActionType,
    string ActionValue,
    string Description);
