using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Autobahn.Persistence.Migrations
{
    /// <inheritdoc/>
    public partial class ProviderAccess : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "Provider", table: "model_routes", type: "TEXT", maxLength: 64, nullable: true);
            migrationBuilder.CreateTable(name: "provider_access", columns: table => new { Id = table.Column<Guid>(type: "TEXT", nullable: false), TenantId = table.Column<Guid>(type: "TEXT", nullable: false), ApiKeyId = table.Column<Guid>(type: "TEXT", nullable: true), Provider = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false), IsOpen = table.Column<bool>(type: "INTEGER", nullable: false), UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_provider_access", x => x.Id);
                table.ForeignKey(name: "FK_provider_access_api_keys_ApiKeyId", column: x => x.ApiKeyId, principalTable: "api_keys", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
                table.ForeignKey(name: "FK_provider_access_tenants_TenantId", column: x => x.TenantId, principalTable: "tenants", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });
            migrationBuilder.CreateIndex(name: "IX_provider_access_ApiKeyId", table: "provider_access", column: "ApiKeyId");
            migrationBuilder.CreateIndex(name: "IX_provider_access_TenantId_Provider_ApiKeyId", table: "provider_access", columns: new[] { "TenantId", "Provider", "ApiKeyId" });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "provider_access");
            migrationBuilder.DropColumn(name: "Provider", table: "model_routes");
        }
    }
}
