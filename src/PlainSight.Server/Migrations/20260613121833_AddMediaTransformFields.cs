using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaTransformFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SourceContentItemId",
                table: "ContentItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailFileName",
                table: "ContentItems",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SourceContentItemId",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ThumbnailFileName",
                table: "ContentItems");
        }
    }
}
