using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBroadcasts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "broadcasts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tags_json = table.Column<string>(type: "jsonb", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    media_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    media_preview_data_uri = table.Column<string>(type: "text", nullable: true),
                    button_title = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    button_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    flow_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scheduled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    audience_count = table.Column<int>(type: "integer", nullable: false),
                    stop_requested = table.Column<bool>(type: "boolean", nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    job_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broadcasts", x => x.id);
                    table.ForeignKey(
                        name: "FK_broadcasts_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_broadcasts_flows_flow_id",
                        column: x => x.flow_id,
                        principalTable: "flows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "broadcast_recipients",
                columns: table => new
                {
                    broadcast_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_broadcast_recipients", x => new { x.broadcast_id, x.contact_id });
                    table.ForeignKey(
                        name: "FK_broadcast_recipients_broadcasts_broadcast_id",
                        column: x => x.broadcast_id,
                        principalTable: "broadcasts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_broadcast_recipients_conversations_contact_id",
                        column: x => x.contact_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_broadcast_recipients_contact_id_sent_at",
                table: "broadcast_recipients",
                columns: new[] { "contact_id", "sent_at" });

            migrationBuilder.CreateIndex(
                name: "IX_broadcasts_channel_id_created_at",
                table: "broadcasts",
                columns: new[] { "channel_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_broadcasts_channel_id_status",
                table: "broadcasts",
                columns: new[] { "channel_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_broadcasts_flow_id",
                table: "broadcasts",
                column: "flow_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "broadcast_recipients");

            migrationBuilder.DropTable(
                name: "broadcasts");
        }
    }
}
