using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInstagramComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "instagram_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    media_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    parent_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    author_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    author_username = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    commented_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_own = table.Column<bool>(type: "boolean", nullable: false),
                    posted_by_automation = table.Column<bool>(type: "boolean", nullable: false),
                    is_hidden = table.Column<bool>(type: "boolean", nullable: false),
                    private_reply_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    is_read = table.Column<bool>(type: "boolean", nullable: false),
                    auto_reply_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instagram_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_instagram_comments_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_instagram_comments_channel_id_external_id",
                table: "instagram_comments",
                columns: new[] { "channel_id", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_instagram_comments_channel_id_is_read",
                table: "instagram_comments",
                columns: new[] { "channel_id", "is_read" });

            migrationBuilder.CreateIndex(
                name: "IX_instagram_comments_channel_id_media_external_id_commented_at",
                table: "instagram_comments",
                columns: new[] { "channel_id", "media_external_id", "commented_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instagram_comments");
        }
    }
}
