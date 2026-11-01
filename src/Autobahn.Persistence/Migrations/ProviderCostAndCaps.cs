using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Autobahn.Persistence.Migrations
{
    /// <inheritdoc/>
    public partial class ProviderCostAndCaps : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(name: "CachedInputPricePerMillionTokens", table: "rate_cards", type: "TEXT", precision: 18, scale: 6, nullable: true);
            migrationBuilder.AddColumn<decimal>(name: "PricePerRequest", table: "rate_cards", type: "TEXT", precision: 18, scale: 6, nullable: false, defaultValue: 0m);
            migrationBuilder.AddColumn<decimal>(name: "MonthlyCapUsd", table: "provider_access", type: "TEXT", precision: 18, scale: 6, nullable: true);
            migrationBuilder.AddColumn<long>(name: "CachedPromptTokens", table: "billing_events", type: "INTEGER", nullable: false, defaultValue: 0L);
            migrationBuilder.AddColumn<string>(name: "CostSource", table: "billing_events", type: "TEXT", maxLength: 16, nullable: true);
            migrationBuilder.AddColumn<string>(name: "Provider", table: "billing_events", type: "TEXT", maxLength: 64, nullable: true);
            migrationBuilder.CreateTable(name: "provider_settings", columns: table => new { Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false), LowBalanceUsd = table.Column<decimal>(type: "TEXT", precision: 18, scale: 6, nullable: true), NewModelsStartStopped = table.Column<bool>(type: "INTEGER", nullable: false), UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_provider_settings", x => x.Provider);
            });
            migrationBuilder.CreateTable(name: "provider_spend_daily", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), UsageDate = table.Column<DateOnly>(type: "TEXT", nullable: false), TenantId = table.Column<Guid>(type: "TEXT", nullable: false), ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: false), Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false), ModelId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, collation: "NOCASE"), Cost = table.Column<decimal>(type: "TEXT", precision: 18, scale: 10, nullable: false), RequestCount = table.Column<int>(type: "INTEGER", nullable: false), UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_provider_spend_daily", x => x.Id);
            });
            migrationBuilder.CreateIndex(name: "IX_provider_spend_daily_Provider_UsageDate", table: "provider_spend_daily", columns: new[] { "Provider", "UsageDate" });
            migrationBuilder.CreateIndex(name: "IX_provider_spend_daily_UsageDate_TenantId_ApiKeyId_Provider_ModelId", table: "provider_spend_daily", columns: new[] { "UsageDate", "TenantId", "ApiKeyId", "Provider", "ModelId" }, unique: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "provider_settings");
            migrationBuilder.DropTable(name: "provider_spend_daily");
            migrationBuilder.DropColumn(name: "CachedInputPricePerMillionTokens", table: "rate_cards");
            migrationBuilder.DropColumn(name: "PricePerRequest", table: "rate_cards");
            migrationBuilder.DropColumn(name: "MonthlyCapUsd", table: "provider_access");
            migrationBuilder.DropColumn(name: "CachedPromptTokens", table: "billing_events");
            migrationBuilder.DropColumn(name: "CostSource", table: "billing_events");
            migrationBuilder.DropColumn(name: "Provider", table: "billing_events");
        }
    }
}
