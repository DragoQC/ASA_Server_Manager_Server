using AsaServerManager.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AsaServerManager.Web.Data.Configurations;

public sealed class ActionMappingEntityConfiguration : IEntityTypeConfiguration<ActionMappingEntity>
{
    public void Configure(EntityTypeBuilder<ActionMappingEntity> builder)
    {
        builder.ToTable("ActionMappings");

        builder.HasKey(mapping => mapping.Id);

        builder.Property(mapping => mapping.CommandText)
            .HasMaxLength(64);

        builder.Property(mapping => mapping.NormalizedCommandText)
            .HasMaxLength(64);

        builder.Property(mapping => mapping.ActionType)
            .HasMaxLength(64);

        builder.Property(mapping => mapping.ActionValue)
            .HasMaxLength(512);

        builder.Property(mapping => mapping.Description)
            .HasMaxLength(256);

        builder.Property(mapping => mapping.CreatedAtUtc);

        builder.Property(mapping => mapping.ModifiedAtUtc);

        builder.HasIndex(mapping => mapping.NormalizedCommandText)
            .IsUnique();

        builder.HasData(ActionMappingSeedData.Create());
    }
}
