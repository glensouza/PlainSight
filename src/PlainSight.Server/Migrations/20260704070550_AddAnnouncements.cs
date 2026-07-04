using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ContentItemId",
                table: "PlaylistItems",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "AnnouncementId",
                table: "PlaylistItems",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Announcements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    EventDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announcements", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AnnouncementMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AnnouncementId = table.Column<int>(type: "integer", nullable: false),
                    ContentItemId = table.Column<int>(type: "integer", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AnnouncementMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AnnouncementMedia_Announcements_AnnouncementId",
                        column: x => x.AnnouncementId,
                        principalTable: "Announcements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AnnouncementMedia_ContentItems_ContentItemId",
                        column: x => x.ContentItemId,
                        principalTable: "ContentItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_AnnouncementId",
                table: "PlaylistItems",
                column: "AnnouncementId");

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementMedia_AnnouncementId_SortOrder",
                table: "AnnouncementMedia",
                columns: new[] { "AnnouncementId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_AnnouncementMedia_ContentItemId",
                table: "AnnouncementMedia",
                column: "ContentItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlaylistItems_Announcements_AnnouncementId",
                table: "PlaylistItems",
                column: "AnnouncementId",
                principalTable: "Announcements",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Data migration: for every ContentItem that had a companion pair, create an Announcement
            // grouping the pair (in the original Before/After order) and repoint any PlaylistItem that
            // referenced the "main" ContentItem at the new Announcement instead, so heartbeat delivery
            // is preserved losslessly. Must run before the old companion columns are dropped below.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    r RECORD;
                    new_announcement_id INTEGER;
                BEGIN
                    FOR r IN
                        SELECT ""Id"" AS main_id, ""Name"" AS main_name, ""CompanionContentItemId"" AS companion_id, ""CompanionPosition"" AS position
                        FROM ""ContentItems""
                        WHERE ""CompanionContentItemId"" IS NOT NULL
                    LOOP
                        INSERT INTO ""Announcements"" (""Title"", ""CreatedAt"", ""UpdatedAt"")
                        VALUES (r.main_name, NOW(), NOW())
                        RETURNING ""Id"" INTO new_announcement_id;

                        IF r.position = 0 THEN
                            INSERT INTO ""AnnouncementMedia"" (""AnnouncementId"", ""ContentItemId"", ""SortOrder"") VALUES
                                (new_announcement_id, r.companion_id, 0),
                                (new_announcement_id, r.main_id, 1);
                        ELSE
                            INSERT INTO ""AnnouncementMedia"" (""AnnouncementId"", ""ContentItemId"", ""SortOrder"") VALUES
                                (new_announcement_id, r.main_id, 0),
                                (new_announcement_id, r.companion_id, 1);
                        END IF;

                        UPDATE ""PlaylistItems""
                        SET ""AnnouncementId"" = new_announcement_id, ""ContentItemId"" = NULL
                        WHERE ""ContentItemId"" = r.main_id;
                    END LOOP;
                END $$;
            ");

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

            migrationBuilder.Sql(@"
                ALTER TABLE ""PlaylistItems"" ADD CONSTRAINT ""CK_PlaylistItems_ExactlyOneTarget""
                CHECK ((""ContentItemId"" IS NOT NULL AND ""AnnouncementId"" IS NULL) OR (""ContentItemId"" IS NULL AND ""AnnouncementId"" IS NOT NULL));
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: rolling back drops Announcements/AnnouncementMedia entirely; companion pairs
            // are not reconstituted from them (lossy downgrade, same as the scaffolded warning).
            migrationBuilder.Sql(@"ALTER TABLE ""PlaylistItems"" DROP CONSTRAINT IF EXISTS ""CK_PlaylistItems_ExactlyOneTarget"";");

            migrationBuilder.DropForeignKey(
                name: "FK_PlaylistItems_Announcements_AnnouncementId",
                table: "PlaylistItems");

            migrationBuilder.DropTable(
                name: "AnnouncementMedia");

            migrationBuilder.DropTable(
                name: "Announcements");

            migrationBuilder.DropIndex(
                name: "IX_PlaylistItems_AnnouncementId",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "AnnouncementId",
                table: "PlaylistItems");

            migrationBuilder.AlterColumn<int>(
                name: "ContentItemId",
                table: "PlaylistItems",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

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
    }
}
