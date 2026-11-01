using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Autobahn.Persistence.Migrations
{
    /// <inheritdoc/>
    public partial class ProviderBalanceReadings : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "provider_balance_readings", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false), ReadAt = table.Column<long>(type: "INTEGER", nullable: false), Remaining = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: false), Currency = table.Column<string>(type: "TEXT", maxLength: 8, nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_provider_balance_readings", x => x.Id);
            });
            migrationBuilder.CreateIndex(name: "IX_billing_events_Provider_RecordedAt", table: "billing_events", columns: new[] { "Provider", "RecordedAt" });
            migrationBuilder.CreateIndex(name: "IX_provider_balance_readings_Provider_ReadAt", table: "provider_balance_readings", columns: new[] { "Provider", "ReadAt" });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "provider_balance_readings");
            migrationBuilder.DropIndex(name: "IX_billing_events_Provider_RecordedAt", table: "billing_events");
        }
    }
}
