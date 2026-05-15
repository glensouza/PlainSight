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
            // Clear old branding schedules
            migrationBuilder.Sql("DELETE FROM \"BrandingSchedules\"");

            // Delete old branding video if it exists
            migrationBuilder.Sql(
                "DELETE FROM \"BrandingVideos\" WHERE \"FileName\" = 'CSDAC Crystal Glass Logo Reveal.mp4' OR \"FileName\" = 'CSDAC Discoid Logo.mp4'");

            // Insert new branding videos
            migrationBuilder.Sql(
                "INSERT INTO \"BrandingVideos\" (\"Name\", \"FileName\", \"FileSizeBytes\", \"DurationSeconds\", \"IsDefault\", \"UploadedAt\") " +
                "VALUES ('Pixel Logo', 'Pixel Logo.mp4', 0, 7, true, NOW())");

            migrationBuilder.Sql(
                "INSERT INTO \"BrandingVideos\" (\"Name\", \"FileName\", \"FileSizeBytes\", \"DurationSeconds\", \"IsDefault\", \"UploadedAt\") " +
                "VALUES ('Discoid Logo', 'Discoid Logo.mp4', 0, 7, false, NOW())");

            // Create an active schedule for the default branding (Pixel Logo) for all days, all times
            migrationBuilder.Sql(
                "INSERT INTO \"BrandingSchedules\" (\"BrandingVideoId\", \"DaysOfWeek\", \"StartTime\", \"EndTime\", \"IsActive\", \"GroupName\", \"CreatedAt\", \"UpdatedAt\") " +
                "SELECT id, 127, '00:00:00', '23:59:59', true, 'Default', NOW(), NOW() FROM \"BrandingVideos\" WHERE \"FileName\" = 'Pixel Logo.mp4'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM \"BrandingSchedules\"");
            migrationBuilder.Sql("DELETE FROM \"BrandingVideos\" WHERE \"FileName\" IN ('Pixel Logo.mp4', 'Discoid Logo.mp4')");
        }
    }
}
