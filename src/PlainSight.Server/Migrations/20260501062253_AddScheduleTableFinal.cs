using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleTableFinal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceGroupVersions_Playlists_DefaultPlaylistId",
                table: "DeviceGroupVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_PlaylistItems_ContentItems_ContentItemId",
                table: "PlaylistItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ScheduleTargetGroup_Schedules_ScheduleId",
                table: "ScheduleTargetGroup");

            migrationBuilder.DropIndex(
                name: "IX_PlayerVersions_VersionNumber",
                table: "PlayerVersions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ScheduleTargetGroup",
                table: "ScheduleTargetGroup");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleTargetGroup_ScheduleId_GroupName",
                table: "ScheduleTargetGroup");

            migrationBuilder.DeleteData(
                table: "DeviceGroupVersions",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.RenameTable(
                name: "ScheduleTargetGroup",
                newName: "ScheduleTargetGroups");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Schedules",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Schedules",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScheduleTargetGroups",
                table: "ScheduleTargetGroups",
                column: "Id");

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
                columns: new[] { "Id", "Description", "Name" },
                values: new object[] { 1, null, "Default" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTargetGroups_ScheduleId",
                table: "ScheduleTargetGroups",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceGroups_Name",
                table: "DeviceGroups",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceGroupVersions_Playlists_DefaultPlaylistId",
                table: "DeviceGroupVersions",
                column: "DefaultPlaylistId",
                principalTable: "Playlists",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_PlaylistItems_ContentItems_ContentItemId",
                table: "PlaylistItems",
                column: "ContentItemId",
                principalTable: "ContentItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduleTargetGroups_Schedules_ScheduleId",
                table: "ScheduleTargetGroups",
                column: "ScheduleId",
                principalTable: "Schedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceGroupVersions_Playlists_DefaultPlaylistId",
                table: "DeviceGroupVersions");

            migrationBuilder.DropForeignKey(
                name: "FK_PlaylistItems_ContentItems_ContentItemId",
                table: "PlaylistItems");

            migrationBuilder.DropForeignKey(
                name: "FK_ScheduleTargetGroups_Schedules_ScheduleId",
                table: "ScheduleTargetGroups");

            migrationBuilder.DropTable(
                name: "DeviceGroups");

            migrationBuilder.DropPrimaryKey(
                name: "PK_ScheduleTargetGroups",
                table: "ScheduleTargetGroups");

            migrationBuilder.DropIndex(
                name: "IX_ScheduleTargetGroups_ScheduleId",
                table: "ScheduleTargetGroups");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Schedules");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Schedules");

            migrationBuilder.RenameTable(
                name: "ScheduleTargetGroups",
                newName: "ScheduleTargetGroup");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScheduleTargetGroup",
                table: "ScheduleTargetGroup",
                column: "Id");

            migrationBuilder.InsertData(
                table: "DeviceGroupVersions",
                columns: new[] { "Id", "DefaultPlaylistId", "GroupName", "TargetVersion" },
                values: new object[] { 1, null, "Default", "1.0.0" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerVersions_VersionNumber",
                table: "PlayerVersions",
                column: "VersionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTargetGroup_ScheduleId_GroupName",
                table: "ScheduleTargetGroup",
                columns: new[] { "ScheduleId", "GroupName" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceGroupVersions_Playlists_DefaultPlaylistId",
                table: "DeviceGroupVersions",
                column: "DefaultPlaylistId",
                principalTable: "Playlists",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_PlaylistItems_ContentItems_ContentItemId",
                table: "PlaylistItems",
                column: "ContentItemId",
                principalTable: "ContentItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ScheduleTargetGroup_Schedules_ScheduleId",
                table: "ScheduleTargetGroup",
                column: "ScheduleId",
                principalTable: "Schedules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
