using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signage.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminUserRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Role",
                table: "AdminUsers",
                type: "integer",
                nullable: false,
                defaultValue: 1); // Admin

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AdminUsers",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Role", table: "AdminUsers");
            migrationBuilder.DropColumn(name: "MustChangePassword", table: "AdminUsers");
        }
    }
}
