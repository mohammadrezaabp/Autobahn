using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configuration;
internal sealed class ProviderBalanceReadingEntityConfiguration : IEntityTypeConfiguration<ProviderBalanceReadingEntity>
{
    public void Configure(EntityTypeBuilder<ProviderBalanceReadingEntity> builder)
    {
        builder.ToTable("provider_balance_readings");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Provider).HasMaxLength(64).IsRequired();
        builder.Property(r => r.Currency).HasMaxLength(8).IsRequired();
        builder.Property(r => r.Remaining).HasPrecision(18, 6);
        builder.HasIndex(r => new { r.Provider, r.ReadAt });
    }
}
