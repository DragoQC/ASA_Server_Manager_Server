using AsaServerManager.Web.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AsaServerManager.Web.Data.Configurations;

public sealed class RemoteKeyEntityConfiguration : IEntityTypeConfiguration<RemoteKeyEntity>
{
    public void Configure(EntityTypeBuilder<RemoteKeyEntity> builder)
    {
        builder.ToTable("RemoteKeys");

        builder.HasKey(remoteKey => remoteKey.Id);

        builder.Property(remoteKey => remoteKey.Id)
            .ValueGeneratedNever();

        builder.Property(remoteKey => remoteKey.CreatedAtUtc);

        builder.Property(remoteKey => remoteKey.ModifiedAtUtc);

        builder.Property(remoteKey => remoteKey.ApiKey)
            .HasMaxLength(256)
            .IsRequired();
    }
}
