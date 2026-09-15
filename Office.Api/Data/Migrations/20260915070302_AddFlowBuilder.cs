using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlowBuilder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "contact_tags",
                columns: table => new
                {
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_tags", x => new { x.contact_id, x.tag });
                    table.ForeignKey(
                        name: "FK_contact_tags_conversations_contact_id",
                        column: x => x.contact_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "contact_variables",
                columns: table => new
                {
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_contact_variables", x => new { x.contact_id, x.key });
                    table.ForeignKey(
                        name: "FK_contact_variables_conversations_contact_id",
                        column: x => x.contact_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flow_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    definition_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_templates", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "flows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    trigger_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    trigger_config_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flows", x => x.id);
                    table.ForeignKey(
                        name: "FK_flows_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flow_edges",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    flow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_port = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    to_node_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_edges", x => x.id);
                    table.ForeignKey(
                        name: "FK_flow_edges_flows_flow_id",
                        column: x => x.flow_id,
                        principalTable: "flows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flow_nodes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    flow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    config_json = table.Column<string>(type: "jsonb", nullable: false),
                    x = table.Column<double>(type: "double precision", nullable: false),
                    y = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_nodes", x => x.id);
                    table.ForeignKey(
                        name: "FK_flow_nodes_flows_flow_id",
                        column: x => x.flow_id,
                        principalTable: "flows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flow_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    flow_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    trigger_external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    current_node_id = table.Column<Guid>(type: "uuid", nullable: true),
                    variables_json = table.Column<string>(type: "jsonb", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    wait_reason = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    resume_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    scheduled_job_id = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    step_count = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_sessions", x => x.id);
                    table.ForeignKey(
                        name: "FK_flow_sessions_conversations_contact_id",
                        column: x => x.contact_id,
                        principalTable: "conversations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_flow_sessions_flows_flow_id",
                        column: x => x.flow_id,
                        principalTable: "flows",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "flow_session_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    node_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_port = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_flow_session_steps", x => x.id);
                    table.ForeignKey(
                        name: "FK_flow_session_steps_flow_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "flow_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_flow_edges_flow_id_from_node_id",
                table: "flow_edges",
                columns: new[] { "flow_id", "from_node_id" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_nodes_flow_id",
                table: "flow_nodes",
                column: "flow_id");

            migrationBuilder.CreateIndex(
                name: "IX_flow_session_steps_node_id",
                table: "flow_session_steps",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "IX_flow_session_steps_session_id",
                table: "flow_session_steps",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_flow_sessions_contact_id",
                table: "flow_sessions",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "IX_flow_sessions_flow_id_contact_id_status",
                table: "flow_sessions",
                columns: new[] { "flow_id", "contact_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_sessions_flow_id_trigger_external_id",
                table: "flow_sessions",
                columns: new[] { "flow_id", "trigger_external_id" });

            migrationBuilder.CreateIndex(
                name: "IX_flow_sessions_resume_at",
                table: "flow_sessions",
                column: "resume_at");

            migrationBuilder.CreateIndex(
                name: "IX_flows_channel_id",
                table: "flows",
                column: "channel_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "contact_tags");

            migrationBuilder.DropTable(
                name: "contact_variables");

            migrationBuilder.DropTable(
                name: "flow_edges");

            migrationBuilder.DropTable(
                name: "flow_nodes");

            migrationBuilder.DropTable(
                name: "flow_session_steps");

            migrationBuilder.DropTable(
                name: "flow_templates");

            migrationBuilder.DropTable(
                name: "flow_sessions");

            migrationBuilder.DropTable(
                name: "flows");
        }
    }
}
