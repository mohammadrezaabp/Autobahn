using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configuration;

internal sealed class ProviderAccessEntityConfiguration : IEntityTypeConfiguration<ProviderAccessEntity>
{
    public void Configure(EntityTypeBuilder<ProviderAccessEntity> builder)
    {
        builder.ToTable("provider_access");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Provider)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(p => p.MonthlyCapUsd)
            .HasPrecision(18, 6);

        // Not unique: SQLite treats every NULL ApiKeyId as distinct, so the index could not hold
        // a tenant to one row per provider. The repository writes through one row instead.
        builder.HasIndex(p => new { p.TenantId, p.Provider, p.ApiKeyId });

        builder.HasOne(p => p.Tenant)
            .WithMany()
            .HasForeignKey(p => p.TenantId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(p => p.ApiKey)
            .WithMany()
            .HasForeignKey(p => p.ApiKeyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
