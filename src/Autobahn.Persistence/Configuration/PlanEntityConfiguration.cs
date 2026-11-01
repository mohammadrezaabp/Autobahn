using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configuration;

internal sealed class PlanEntityConfiguration : IEntityTypeConfiguration<PlanEntity>
{
    public void Configure(EntityTypeBuilder<PlanEntity> builder)
    {
        builder.ToTable("plans");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Slug)
            .HasMaxLength(64)
            .IsRequired();

        builder.HasIndex(p => p.Slug)
            .IsUnique();

        builder.Property(p => p.Name)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(p => p.RateCardSlug)
            .HasMaxLength(64);
    }
}
