using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceStallTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentlyPlayingExpectedDurationSeconds",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CurrentlyPlayingSince",
                table: "Devices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsStalledAlertSent",
                table: "Devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentlyPlayingExpectedDurationSeconds",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "CurrentlyPlayingSince",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "IsStalledAlertSent",
                table: "Devices");
        }
    }
}
