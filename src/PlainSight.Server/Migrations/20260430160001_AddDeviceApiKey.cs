using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceApiKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApiKey",
                table: "Devices",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApiKey",
                table: "Devices");
        }
    }
}
