using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Autobahn.Persistence.Entities;

namespace Autobahn.Persistence.Configuration;

internal sealed class MaintenanceStateEntityConfiguration : IEntityTypeConfiguration<MaintenanceStateEntity>
{
    public void Configure(EntityTypeBuilder<MaintenanceStateEntity> builder)
    {
        builder.ToTable("maintenance_state");

        builder.HasKey(m => m.Key);

        builder.Property(m => m.Key)
            .HasMaxLength(128);

        builder.Property(m => m.ValueJson)
            .IsRequired();
    }
}
