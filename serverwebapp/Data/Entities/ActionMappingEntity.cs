namespace AsaServerManager.Web.Data.Entities;

public sealed class ActionMappingEntity : BaseEntity
{
    public required string CommandText { get; set; }
    public required string NormalizedCommandText { get; set; }
    public required string ActionType { get; set; }
    public string ActionValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
