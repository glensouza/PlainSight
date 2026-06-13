using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddContentItemCompanion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CompanionContentItemId",
                table: "ContentItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanionPosition",
                table: "ContentItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentItems_CompanionContentItemId",
                table: "ContentItems",
                column: "CompanionContentItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_ContentItems_ContentItems_CompanionContentItemId",
                table: "ContentItems",
                column: "CompanionContentItemId",
                principalTable: "ContentItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ContentItems_ContentItems_CompanionContentItemId",
                table: "ContentItems");

            migrationBuilder.DropIndex(
                name: "IX_ContentItems_CompanionContentItemId",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "CompanionContentItemId",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "CompanionPosition",
                table: "ContentItems");
        }
    }
}
