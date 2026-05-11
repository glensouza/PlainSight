using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleAutoScreenshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoScreenshotEnabled",
                table: "Schedules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ScreenshotBurstCount",
                table: "Schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ScreenshotBurstIntervalSeconds",
                table: "Schedules",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoScreenshotEnabled",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "ScreenshotBurstCount",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "ScreenshotBurstIntervalSeconds",
                table: "Schedules");
        }
    }
}
