using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using AsaServerManager.Web.Models.Email;

namespace AsaServerManager.Web.Services;

public sealed class EmailService(EmailSettingsService emailSettingsService)
{
    private readonly EmailSettingsService _emailSettingsService = emailSettingsService;

    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        SmtpEmailSettings settings = await _emailSettingsService.LoadAsync(cancellationToken);
        await SendAsync(settings, message, cancellationToken);
    }

    public async Task SendAsync(SmtpEmailSettings settings, EmailMessage message, CancellationToken cancellationToken = default)
    {
        ValidateSettings(settings);

        string? senderEmail = string.IsNullOrWhiteSpace(message.Sender) ? settings.FromEmail : message.Sender;

        using MailMessage mailMessage = new()
        {
            Subject = message.Subject,
            From = new MailAddress(senderEmail!, settings.FromName),
            Body = message.Body,
            IsBodyHtml = true
        };

        mailMessage.To.Add(message.Receiver);

        if (message.InlineAssets is { Count: > 0 })
        {
            AlternateView htmlView = AlternateView.CreateAlternateViewFromString(
                message.Body,
                null,
                MediaTypeNames.Text.Html);

            foreach (EmailInlineAsset inlineAsset in message.InlineAssets)
            {
                LinkedResource linkedResource = new(inlineAsset.FilePath, inlineAsset.MediaType)
                {
                    ContentId = inlineAsset.ContentId,
                    TransferEncoding = TransferEncoding.Base64
                };

                htmlView.LinkedResources.Add(linkedResource);
            }

            mailMessage.AlternateViews.Add(htmlView);
        }

        using SmtpClient smtpClient = new(settings.SmtpHost, settings.SmtpPort)
        {
            EnableSsl = settings.SmtpPort is 465 or 587,
            Credentials = new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword)
        };

        await smtpClient.SendMailAsync(mailMessage, cancellationToken);
    }

    private static void ValidateSettings(SmtpEmailSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SmtpHost) ||
            settings.SmtpPort <= 0 ||
            string.IsNullOrWhiteSpace(settings.SmtpUsername) ||
            string.IsNullOrWhiteSpace(settings.SmtpPassword) ||
            string.IsNullOrWhiteSpace(settings.FromEmail))
        {
            throw new InvalidOperationException("Email configuration is incomplete.");
        }
    }
}
