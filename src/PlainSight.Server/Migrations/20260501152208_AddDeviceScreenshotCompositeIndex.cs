using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceScreenshotCompositeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceScreenshots_DeviceId",
                table: "DeviceScreenshots");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceScreenshots_DeviceId_CapturedAt",
                table: "DeviceScreenshots",
                columns: new[] { "DeviceId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceScreenshots_DeviceId_CapturedAt",
                table: "DeviceScreenshots");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceScreenshots_DeviceId",
                table: "DeviceScreenshots",
                column: "DeviceId");
        }
    }
}
