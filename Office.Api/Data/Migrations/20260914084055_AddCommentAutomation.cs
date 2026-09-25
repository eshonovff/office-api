using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentAutomation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "automation_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trigger_config_json = table.Column<string>(type: "jsonb", nullable: false),
                    condition_config_json = table.Column<string>(type: "jsonb", nullable: false),
                    action_config_json = table.Column<string>(type: "jsonb", nullable: false),
                    cooldown_minutes = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_rules", x => x.id);
                    table.ForeignKey(
                        name: "FK_automation_rules_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "automation_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    actor_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_media_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    matched_keyword = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    comment_reply_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    dm_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_automation_runs", x => x.id);
                    table.ForeignKey(
                        name: "FK_automation_runs_automation_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "automation_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_automation_rules_channel_id",
                table: "automation_rules",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "IX_automation_runs_rule_id_actor_external_id_target_media_exte~",
                table: "automation_runs",
                columns: new[] { "rule_id", "actor_external_id", "target_media_external_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "automation_runs");

            migrationBuilder.DropTable(
                name: "automation_rules");
        }
    }
}
