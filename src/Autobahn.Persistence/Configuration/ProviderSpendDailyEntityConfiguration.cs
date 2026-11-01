using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configuration;
internal sealed class ProviderSpendDailyEntityConfiguration : IEntityTypeConfiguration<ProviderSpendDailyEntity>
{
    public void Configure(EntityTypeBuilder<ProviderSpendDailyEntity> builder)
    {
        builder.ToTable("provider_spend_daily");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Provider).HasMaxLength(64).IsRequired();
        builder.Property(s => s.ModelId).HasMaxLength(256).UseCollation("NOCASE").IsRequired();
        // No foreign keys on purpose: a month's spend has to outlive the key that made it, or
        // deleting a key would hand its tenant the headroom back.
        builder.Property(s => s.TenantId).IsRequired();
        builder.Property(s => s.ApiKeyId).IsRequired();
        // Wider than the ledger's cost columns: a cheap model's request costs less than a
        // millionth of a dollar, and rounding each one to zero would leave a cap never reached.
        builder.Property(s => s.Cost).HasPrecision(18, 10);
        builder.HasIndex(s => new { s.UsageDate, s.TenantId, s.ApiKeyId, s.Provider, s.ModelId }).IsUnique();
        builder.HasIndex(s => new { s.Provider, s.UsageDate });
    }
}
