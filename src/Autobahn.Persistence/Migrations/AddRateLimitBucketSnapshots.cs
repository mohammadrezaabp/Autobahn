using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
namespace Autobahn.Persistence.Migrations
{
    /// <inheritdoc/>
    public partial class AddRateLimitBucketSnapshots : Migration
    {
        /// <inheritdoc/>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(name: "rate_limit_bucket_snapshots", columns: table => new { PartitionKey = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false), Tokens = table.Column<double>(type: "REAL", nullable: false), Capacity = table.Column<int>(type: "INTEGER", nullable: false), AsOfUtc = table.Column<long>(type: "INTEGER", nullable: false) }, constraints: table =>
            {
                table.PrimaryKey("PK_rate_limit_bucket_snapshots", x => x.PartitionKey);
            });
        }

        /// <inheritdoc/>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "rate_limit_bucket_snapshots");
        }
    }
}
