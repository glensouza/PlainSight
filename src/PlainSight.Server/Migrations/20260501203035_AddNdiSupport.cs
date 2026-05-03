using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddNdiSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AssignedNdiSourceId",
                table: "Devices",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LiveModeOverride",
                table: "Devices",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "NdiAutoSwitch",
                table: "Devices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "NdiSources",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServiceName = table.Column<string>(type: "text", nullable: false),
                    HostName = table.Column<string>(type: "text", nullable: true),
                    IpAddress = table.Column<string>(type: "text", nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    FirstSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NdiSources", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Devices_AssignedNdiSourceId",
                table: "Devices",
                column: "AssignedNdiSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_NdiSources_ServiceName",
                table: "NdiSources",
                column: "ServiceName",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_NdiSources_AssignedNdiSourceId",
                table: "Devices",
                column: "AssignedNdiSourceId",
                principalTable: "NdiSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_NdiSources_AssignedNdiSourceId",
                table: "Devices");

            migrationBuilder.DropTable(
                name: "NdiSources");

            migrationBuilder.DropIndex(
                name: "IX_Devices_AssignedNdiSourceId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "AssignedNdiSourceId",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "LiveModeOverride",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "NdiAutoSwitch",
                table: "Devices");
        }
    }
}
