namespace asa_server_node_api.Data.Entities;

public sealed class ActionMappingEntity : BaseEntity
{
    public required string CommandText { get; set; }
    public required string NormalizedCommandText { get; set; }
    public required string ActionType { get; set; }
    public string ActionValue { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
