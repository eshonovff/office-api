using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "flow_conversions",
                columns: table => new
                {
                    flow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_conversions", x => new { x.flow_id, x.contact_id });
                    table.ForeignKey(
                        name: "FK_flow_conversions_conversations_contact_id",
                        column: x => x.contact_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_flow_conversions_flows_flow_id",
                        column: x => x.flow_id,
                        principalTable: "flows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_instagram_comments_channel_id_commented_at",
                table: "instagram_comments",
                columns: new[] { "channel_id", "commented_at" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_sessions_flow_id_created_at",
                table: "flow_sessions",
                columns: new[] { "flow_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_channel_id_created_at",
                table: "conversations",
                columns: new[] { "channel_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_automation_runs_rule_id_created_at",
                table: "automation_runs",
                columns: new[] { "rule_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_conversions_contact_id",
                table: "flow_conversions",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_flow_conversions_flow_id_created_at",
                table: "flow_conversions",
                columns: new[] { "flow_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "flow_conversions");

            migrationBuilder.DropIndex(
                name: "IX_instagram_comments_channel_id_commented_at",
                table: "instagram_comments");

            migrationBuilder.DropIndex(
                name: "IX_flow_sessions_flow_id_created_at",
                table: "flow_sessions");

            migrationBuilder.DropIndex(
                name: "IX_conversations_channel_id_created_at",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_automation_runs_rule_id_created_at",
                table: "automation_runs");
        }
    }
}
