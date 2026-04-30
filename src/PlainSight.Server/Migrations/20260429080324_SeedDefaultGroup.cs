using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class SeedDefaultGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "DeviceGroupVersions",
                columns: new[] { "Id", "GroupName", "TargetVersion" },
                values: new object[] { 1, "Default", "1.0.0" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "DeviceGroupVersions",
                keyColumn: "Id",
                keyValue: 1);
        }
    }
}
