using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddLogEntryFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LogEntries_Category_Timestamp",
                table: "LogEntries");

            migrationBuilder.AlterColumn<string>(
                name: "Exception",
                table: "LogEntries",
                type: "character varying(8000)",
                maxLength: 8000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CategoryName",
                table: "LogEntries",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LevelOrder",
                table: "LogEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Category_LevelOrder_Timestamp",
                table: "LogEntries",
                columns: new[] { "Category", "LevelOrder", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LogEntries_Category_LevelOrder_Timestamp",
                table: "LogEntries");

            migrationBuilder.DropColumn(
                name: "CategoryName",
                table: "LogEntries");

            migrationBuilder.DropColumn(
                name: "LevelOrder",
                table: "LogEntries");

            migrationBuilder.AlterColumn<string>(
                name: "Exception",
                table: "LogEntries",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(8000)",
                oldMaxLength: 8000,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LogEntries_Category_Timestamp",
                table: "LogEntries",
                columns: new[] { "Category", "Timestamp" });
        }
    }
}
