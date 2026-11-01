using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configurations;

public sealed class QuotaAllocationEntityConfiguration : IEntityTypeConfiguration<QuotaAllocationEntity>
{
    public void Configure(EntityTypeBuilder<QuotaAllocationEntity> builder)
    {
        builder.ToTable("quota_allocations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.SoftLimitRatio)
            .HasPrecision(5, 4);

        builder.HasIndex(x => new { x.TenantId, x.PeriodStart, x.PeriodEnd })
            .IsUnique();
    }
}
