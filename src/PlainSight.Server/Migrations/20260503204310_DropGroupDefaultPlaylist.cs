using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class DropGroupDefaultPlaylist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceGroupVersions_Playlists_DefaultPlaylistId",
                table: "DeviceGroupVersions");

            migrationBuilder.DropIndex(
                name: "IX_DeviceGroupVersions_DefaultPlaylistId",
                table: "DeviceGroupVersions");

            migrationBuilder.DropColumn(
                name: "DefaultPlaylistId",
                table: "DeviceGroupVersions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultPlaylistId",
                table: "DeviceGroupVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceGroupVersions_DefaultPlaylistId",
                table: "DeviceGroupVersions",
                column: "DefaultPlaylistId");

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceGroupVersions_Playlists_DefaultPlaylistId",
                table: "DeviceGroupVersions",
                column: "DefaultPlaylistId",
                principalTable: "Playlists",
                principalColumn: "Id");
        }
    }
}
