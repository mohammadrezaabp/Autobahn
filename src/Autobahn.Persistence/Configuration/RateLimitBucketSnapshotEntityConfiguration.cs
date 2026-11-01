using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configuration;

internal sealed class RateLimitBucketSnapshotEntityConfiguration : IEntityTypeConfiguration<RateLimitBucketSnapshotEntity>
{
    public void Configure(EntityTypeBuilder<RateLimitBucketSnapshotEntity> builder)
    {
        builder.ToTable("rate_limit_bucket_snapshots");

        builder.HasKey(b => b.PartitionKey);

        builder.Property(b => b.PartitionKey)
            .HasMaxLength(512);
    }
}
