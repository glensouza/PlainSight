using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceGroupTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceGroups", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "DeviceGroups",
                columns: new[] { "Id", "Name", "Description" },
                values: new object[] { 1, "Default", null });

            // Explicit Id overrides don't advance the identity sequence in PostgreSQL,
            // so reset it now or the next auto-generated insert will also try Id=1.
            migrationBuilder.Sql(
                @"SELECT setval(pg_get_serial_sequence('""DeviceGroups""', 'Id'), MAX(""Id"")) FROM ""DeviceGroups"";");

            // Migrate any existing group names from DeviceGroupVersions that aren't already in DeviceGroups
            migrationBuilder.Sql(
                @"INSERT INTO ""DeviceGroups"" (""Name"", ""Description"")
                  SELECT DISTINCT ""GroupName"", NULL
                  FROM ""DeviceGroupVersions""
                  WHERE ""GroupName"" != 'Default'
                  AND NOT EXISTS (
                      SELECT 1 FROM ""DeviceGroups"" WHERE ""Name"" = ""DeviceGroupVersions"".""GroupName""
                  )");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceGroups_Name",
                table: "DeviceGroups",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceGroups");
        }
    }
}
