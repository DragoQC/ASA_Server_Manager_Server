namespace AsaServerManager.Web.Data.Entities;

public sealed class EmailSettingsEntity : BaseEntity
{
    public required string SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public required string SmtpUsername { get; set; }
    public required string SmtpPassword { get; set; }
    public required string FromEmail { get; set; }
    public required string FromName { get; set; }
}
