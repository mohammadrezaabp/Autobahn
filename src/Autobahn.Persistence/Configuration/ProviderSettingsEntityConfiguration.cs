using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configuration;
internal sealed class ProviderSettingsEntityConfiguration : IEntityTypeConfiguration<ProviderSettingsEntity>
{
    public void Configure(EntityTypeBuilder<ProviderSettingsEntity> builder)
    {
        builder.ToTable("provider_settings");
        builder.HasKey(s => s.Provider);
        builder.Property(s => s.Provider).HasMaxLength(64);
        builder.Property(s => s.LowBalanceUsd).HasPrecision(18, 6);
    }
}
