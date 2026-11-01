using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configuration;

internal sealed class ModelGrantEntityConfiguration : IEntityTypeConfiguration<ModelGrantEntity>
{
    public void Configure(EntityTypeBuilder<ModelGrantEntity> builder)
    {
        builder.ToTable("model_grants");

        builder.HasKey(g => g.Id);

        builder.Property(g => g.ModelPattern)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(g => g.Effect)
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.HasIndex(g => new { g.TenantId, g.ModelPattern });
    }
}
