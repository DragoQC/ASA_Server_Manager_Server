using System.ComponentModel.DataAnnotations;

namespace AsaServerManager.Web.Models.Email;

public class SmtpEmailSettings
{
    [Required]
    public string? SmtpHost { get; set; }

    [Range(1, 65535)]
    public int SmtpPort { get; set; } = 587;

    [Required]
    public string? SmtpUsername { get; set; }

    [Required]
    public string? SmtpPassword { get; set; }

    [Required]
    [EmailAddress]
    public string? FromEmail { get; set; }

    [Required]
    public string? FromName { get; set; } = "ASA Server Manager";
}
