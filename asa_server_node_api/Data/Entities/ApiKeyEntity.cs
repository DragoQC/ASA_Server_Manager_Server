namespace asa_server_node_api.Data.Entities;

public sealed class ApiKeyEntity : BaseEntity
{
    public required string Type { get; set; }
    public required string ApiKey { get; set; }
}
