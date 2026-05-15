using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlainSight.Server.Migrations
{
    /// <inheritdoc />
    public partial class UpdateBrandingToNewFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Delete schedules only for the old branding videos being replaced
            migrationBuilder.Sql(
                "DELETE FROM \"BrandingSchedules\" WHERE \"BrandingVideoId\" IN (" +
                "  SELECT \"Id\" FROM \"BrandingVideos\" WHERE \"FileName\" IN ('CSDAC Crystal Glass Logo Reveal.mp4', 'CSDAC Discoid Logo.mp4')" +
                ")");

            // Delete old branding videos
            migrationBuilder.Sql(
                "DELETE FROM \"BrandingVideos\" WHERE \"FileName\" IN ('CSDAC Crystal Glass Logo Reveal.mp4', 'CSDAC Discoid Logo.mp4')");

            // Clear IsDefault flag on all existing branding videos before setting new default
            migrationBuilder.Sql("UPDATE \"BrandingVideos\" SET \"IsDefault\" = false");

            // Insert new branding videos using INSERT ... ON CONFLICT to handle existing data
            migrationBuilder.Sql(
                "INSERT INTO \"BrandingVideos\" (\"Name\", \"FileName\", \"FileSizeBytes\", \"DurationSeconds\", \"IsDefault\", \"UploadedAt\") " +
                "VALUES ('Pixel Logo', 'Pixel Logo.mp4', 0, 7, true, NOW()) " +
                "ON CONFLICT (\"FileName\") DO UPDATE SET \"IsDefault\" = true, \"Name\" = 'Pixel Logo'");

            migrationBuilder.Sql(
                "INSERT INTO \"BrandingVideos\" (\"Name\", \"FileName\", \"FileSizeBytes\", \"DurationSeconds\", \"IsDefault\", \"UploadedAt\") " +
                "VALUES ('Discoid Logo', 'Discoid Logo.mp4', 0, 7, false, NOW()) " +
                "ON CONFLICT (\"FileName\") DO UPDATE SET \"Name\" = 'Discoid Logo'");

            // Create an active schedule for the default branding (Pixel Logo) for all days, all times
            migrationBuilder.Sql(
                "INSERT INTO \"BrandingSchedules\" (\"BrandingVideoId\", \"DaysOfWeek\", \"StartTime\", \"EndTime\", \"IsActive\", \"GroupName\", \"CreatedAt\", \"UpdatedAt\") " +
                "SELECT \"Id\", 127, '00:00:00', '23:59:59', true, 'Default', NOW(), NOW() FROM \"BrandingVideos\" WHERE \"FileName\" = 'Pixel Logo.mp4' " +
                "ON CONFLICT DO NOTHING");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only delete schedules that were created by this migration for the new branding videos
            migrationBuilder.Sql(
                "DELETE FROM \"BrandingSchedules\" WHERE \"BrandingVideoId\" IN (" +
                "  SELECT \"Id\" FROM \"BrandingVideos\" WHERE \"FileName\" IN ('Pixel Logo.mp4', 'Discoid Logo.mp4')" +
                ")");
            migrationBuilder.Sql("DELETE FROM \"BrandingVideos\" WHERE \"FileName\" IN ('Pixel Logo.mp4', 'Discoid Logo.mp4')");
        }
    }
}
