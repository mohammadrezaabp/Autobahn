using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Autobahn.Persistence.Migrations
{
    /// <inheritdoc/>
    public partial class AddRateLimitTpm : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(name: "Tpm", table: "rate_limit_rules", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "Tpm", table: "rate_limit_plans", type: "INTEGER", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<int>(name: "Tpm", table: "rate_limit_defaults", type: "INTEGER", nullable: false, defaultValue: 0);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Tpm", table: "rate_limit_rules");
            migrationBuilder.DropColumn(name: "Tpm", table: "rate_limit_plans");
            migrationBuilder.DropColumn(name: "Tpm", table: "rate_limit_defaults");
        }
    }
}
