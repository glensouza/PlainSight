using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceScreenshotFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LatestScreenshotPath",
                table: "Devices",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LatestScreenshotAt",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "LatestScreenshotPath", table: "Devices");
            migrationBuilder.DropColumn(name: "LatestScreenshotAt", table: "Devices");
        }
    }
}
