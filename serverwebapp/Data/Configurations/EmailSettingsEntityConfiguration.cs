using AsaServerManager.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AsaServerManager.Web.Data.Configurations;

public sealed class EmailSettingsEntityConfiguration : IEntityTypeConfiguration<EmailSettingsEntity>
{
    public void Configure(EntityTypeBuilder<EmailSettingsEntity> builder)
    {
        builder.ToTable("EmailSettings");

        builder.HasKey(emailSettings => emailSettings.Id);

        builder.Property(emailSettings => emailSettings.Id)
            .ValueGeneratedNever();

        builder.Property(emailSettings => emailSettings.CreatedAtUtc);

        builder.Property(emailSettings => emailSettings.ModifiedAtUtc);

        builder.Property(emailSettings => emailSettings.SmtpHost)
            .HasMaxLength(256);

        builder.Property(emailSettings => emailSettings.SmtpUsername)
            .HasMaxLength(256);

        builder.Property(emailSettings => emailSettings.SmtpPassword)
            .HasMaxLength(512);

        builder.Property(emailSettings => emailSettings.FromEmail)
            .HasMaxLength(256);

        builder.Property(emailSettings => emailSettings.FromName)
            .HasMaxLength(256);
    }
}
