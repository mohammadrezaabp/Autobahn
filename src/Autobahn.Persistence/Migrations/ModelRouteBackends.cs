using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Autobahn.Persistence.Migrations
{
    /// <inheritdoc/>
    public partial class ModelRouteBackends : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(name: "BackendsJson", table: "model_routes", type: "TEXT", nullable: true);
            migrationBuilder.AddColumn<string>(name: "LabelsJson", table: "model_routes", type: "TEXT", nullable: true);
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "BackendsJson", table: "model_routes");
            migrationBuilder.DropColumn(name: "LabelsJson", table: "model_routes");
        }
    }
}
