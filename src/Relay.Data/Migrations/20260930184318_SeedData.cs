using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Relay.Data.Migrations
{
    /// <summary>Loads sql/seed.sql (embedded) inside the migration's transaction.</summary>
    public partial class SeedData : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(SeedScript.Read());
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM activity_events; DELETE FROM accounts;");
        }
    }
}
