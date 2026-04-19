using AsaServerManager.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AsaServerManager.Web.Data.Configurations;

public sealed class ApiKeyEntityConfiguration : IEntityTypeConfiguration<ApiKeyEntity>
{
    public void Configure(EntityTypeBuilder<ApiKeyEntity> builder)
    {
        builder.ToTable("ApiKeys");

        builder.HasKey(apiKey => apiKey.Id);

        builder.Property(apiKey => apiKey.CreatedAtUtc);

        builder.Property(apiKey => apiKey.ModifiedAtUtc);

        builder.Property(apiKey => apiKey.Type)
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(apiKey => apiKey.ApiKey)
            .HasMaxLength(256)
            .IsRequired();

        builder.HasIndex(apiKey => apiKey.Type)
            .IsUnique();
    }
}
