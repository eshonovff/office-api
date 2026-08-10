using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageMediaFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "media_deleted_at",
                table: "messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "media_download_error",
                table: "messages",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "media_external_id",
                table: "messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mime_type",
                table: "messages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "original_file_name",
                table: "messages",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "size_bytes",
                table: "messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "thumbnail_url",
                table: "messages",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "voice_duration_seconds",
                table: "messages",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "media_deleted_at",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "media_download_error",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "media_external_id",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "mime_type",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "original_file_name",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "size_bytes",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "thumbnail_url",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "voice_duration_seconds",
                table: "messages");
        }
    }
}
