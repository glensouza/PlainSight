using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class PlaylistAnnouncementsOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The exactly-one-target check references ContentItemId, which is dropped below. Remove it
            // first; with content items gone from playlists, AnnouncementId (NOT NULL) is the only target.
            migrationBuilder.Sql(@"ALTER TABLE ""PlaylistItems"" DROP CONSTRAINT IF EXISTS ""CK_PlaylistItems_ExactlyOneTarget"";");

            // Data migration: every PlaylistItem still backed by a ContentItem becomes a single-media
            // Announcement wrapping that content item, and the PlaylistItem is repointed at it — so
            // heartbeat delivery is preserved before playlists become announcement-only. The new
            // Announcement inherits the content item's event date (or the date part of its old expiry).
            // Must run before ContentItemId (and the ContentItems date columns) are dropped below.
            migrationBuilder.Sql(@"
                DO $$
                DECLARE
                    r RECORD;
                    new_announcement_id INTEGER;
                BEGIN
                    FOR r IN
                        SELECT pi.""Id"" AS item_id, ci.""Id"" AS content_id, ci.""Name"" AS content_name,
                               COALESCE(ci.""EventDate"", ci.""ExpiresAt""::date) AS event_date
                        FROM ""PlaylistItems"" pi
                        JOIN ""ContentItems"" ci ON ci.""Id"" = pi.""ContentItemId""
                        WHERE pi.""ContentItemId"" IS NOT NULL
                    LOOP
                        INSERT INTO ""Announcements"" (""Title"", ""EventDate"", ""CreatedAt"", ""UpdatedAt"")
                        VALUES (r.content_name, r.event_date, NOW(), NOW())
                        RETURNING ""Id"" INTO new_announcement_id;

                        INSERT INTO ""AnnouncementMedia"" (""AnnouncementId"", ""ContentItemId"", ""SortOrder"")
                        VALUES (new_announcement_id, r.content_id, 0);

                        UPDATE ""PlaylistItems""
                        SET ""AnnouncementId"" = new_announcement_id, ""ContentItemId"" = NULL
                        WHERE ""Id"" = r.item_id;
                    END LOOP;
                END $$;
            ");

            migrationBuilder.DropForeignKey(
                name: "FK_PlaylistItems_ContentItems_ContentItemId",
                table: "PlaylistItems");

            migrationBuilder.DropIndex(
                name: "IX_PlaylistItems_ContentItemId",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "ContentItemId",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "OverrideDurationSeconds",
                table: "PlaylistItems");

            migrationBuilder.DropColumn(
                name: "EventDate",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "ContentItems");

            migrationBuilder.DropColumn(
                name: "ExpiresAt",
                table: "Announcements");

            migrationBuilder.AlterColumn<int>(
                name: "AnnouncementId",
                table: "PlaylistItems",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "AnnouncementId",
                table: "PlaylistItems",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "ContentItemId",
                table: "PlaylistItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverrideDurationSeconds",
                table: "PlaylistItems",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "EventDate",
                table: "ContentItems",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "ContentItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAt",
                table: "Announcements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlaylistItems_ContentItemId",
                table: "PlaylistItems",
                column: "ContentItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_PlaylistItems_ContentItems_ContentItemId",
                table: "PlaylistItems",
                column: "ContentItemId",
                principalTable: "ContentItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            // Restore the exactly-one-target check (lossy: backfilled announcements are not reverted
            // to content items, but existing rows still satisfy it since AnnouncementId is set).
            migrationBuilder.Sql(@"
                ALTER TABLE ""PlaylistItems"" ADD CONSTRAINT ""CK_PlaylistItems_ExactlyOneTarget""
                CHECK ((""ContentItemId"" IS NOT NULL AND ""AnnouncementId"" IS NULL) OR (""ContentItemId"" IS NULL AND ""AnnouncementId"" IS NOT NULL));
            ");
        }
    }
}
