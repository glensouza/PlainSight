using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class FinalScheduleAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Note: These modifications are necessary to unify the local changes with the merged main branch.
            // Some drops are due to changing onDelete behaviors or explicit schema refinements.

            // 1. Rename the mapping table to match the new pluralized model
            migrationBuilder.RenameTable(
                name: "ScheduleTargetGroup",
                newName: "ScheduleTargetGroups");

            // 2. Add tracking timestamps to the Schedules table
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Schedules",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(2026, 4, 30, 0, 0, 0, 0, DateTimeKind.Utc));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Schedules",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(2026, 4, 30, 0, 0, 0, 0, DateTimeKind.Utc));

            // 3. Update the Primary Key for the renamed table
            migrationBuilder.DropPrimaryKey(
                name: "PK_ScheduleTargetGroup",
                table: "ScheduleTargetGroups");

            migrationBuilder.AddPrimaryKey(
                name: "PK_ScheduleTargetGroups",
                table: "ScheduleTargetGroups",
                column: "Id");

            // 4. Update Indices for the renamed table
            migrationBuilder.DropIndex(
                name: "IX_ScheduleTargetGroup_ScheduleId_GroupName",
                table: "ScheduleTargetGroups");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTargetGroups_ScheduleId",
                table: "ScheduleTargetGroups",
                column: "ScheduleId");

            // 5. Refine Foreign Key for the renamed table
            migrationBuilder.DropForeignKey(
                name: "FK_ScheduleTargetGroup_Schedules_ScheduleId",
                table: "ScheduleTargetGroups");

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
                name: "FK_ScheduleTargetGroups_Schedules_ScheduleId",
                table: "ScheduleTargetGroups");

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

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTargetGroup_ScheduleId_GroupName",
                table: "ScheduleTargetGroup",
                columns: new[] { "ScheduleId", "GroupName" },
                unique: true);

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
