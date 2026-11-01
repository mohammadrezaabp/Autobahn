using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configuration;

internal sealed class CorsSettingsEntityConfiguration : IEntityTypeConfiguration<CorsSettingsEntity>
{
    public void Configure(EntityTypeBuilder<CorsSettingsEntity> builder)
    {
        builder.ToTable("cors_settings");

        builder.HasKey(c => c.Id);

        // Single-row table; the key is assigned explicitly (always 1), never database-generated.
        builder.Property(c => c.Id)
            .ValueGeneratedNever();

        // Mapped to a JSON TEXT column by EF's primitive-collection support on SQLite.
        builder.Property(c => c.AllowedOrigins)
            .IsRequired();
    }
}
