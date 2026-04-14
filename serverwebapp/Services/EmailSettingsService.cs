using AsaServerManager.Web.Data;
using AsaServerManager.Web.Data.Entities;
using AsaServerManager.Web.Models.Email;
using Microsoft.EntityFrameworkCore;

namespace AsaServerManager.Web.Services;

public sealed class EmailSettingsService(IDbContextFactory<AppDbContext> dbContextFactory)
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory = dbContextFactory;

    public async Task<SmtpEmailSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        EmailSettingsEntity? entity = await dbContext.EmailSettings
            .AsNoTracking()
            .SingleOrDefaultAsync(settings => settings.Id == 1, cancellationToken);

        return entity is null
            ? new SmtpEmailSettings()
            : new SmtpEmailSettings
            {
                SmtpHost = entity.SmtpHost,
                SmtpPort = entity.SmtpPort,
                SmtpUsername = entity.SmtpUsername,
                SmtpPassword = entity.SmtpPassword,
                FromEmail = entity.FromEmail,
                FromName = entity.FromName
            };
    }

    public async Task SaveAsync(SmtpEmailSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await using AppDbContext dbContext = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        EmailSettingsEntity? entity = await dbContext.EmailSettings
            .SingleOrDefaultAsync(emailSettings => emailSettings.Id == 1, cancellationToken);

        if (entity is null)
        {
            entity = new EmailSettingsEntity
            {
                Id = 1,
                SmtpHost = settings.SmtpHost ?? string.Empty,
                SmtpUsername = settings.SmtpUsername ?? string.Empty,
                SmtpPassword = settings.SmtpPassword ?? string.Empty,
                FromEmail = settings.FromEmail ?? string.Empty,
                FromName = settings.FromName ?? "ASA Server Manager"
            };
            dbContext.EmailSettings.Add(entity);
        }

        entity.SmtpHost = settings.SmtpHost ?? string.Empty;
        entity.SmtpPort = settings.SmtpPort;
        entity.SmtpUsername = settings.SmtpUsername ?? string.Empty;
        entity.SmtpPassword = settings.SmtpPassword ?? string.Empty;
        entity.FromEmail = settings.FromEmail ?? string.Empty;
        entity.FromName = settings.FromName ?? "ASA Server Manager";

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
