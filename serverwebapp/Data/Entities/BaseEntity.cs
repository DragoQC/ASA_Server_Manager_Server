namespace AsaServerManager.Web.Data.Entities;

public abstract class BaseEntity
{
    public int Id { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset ModifiedAtUtc { get; set; }
}
