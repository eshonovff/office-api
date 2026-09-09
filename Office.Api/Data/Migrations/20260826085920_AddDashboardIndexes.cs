using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_messages_created_at_direction",
                table: "messages",
                columns: new[] { "created_at", "direction" });

            migrationBuilder.CreateIndex(
                name: "IX_messages_delivery_status",
                table: "messages",
                column: "delivery_status");

            migrationBuilder.CreateIndex(
                name: "IX_conversations_status_assigned_to",
                table: "conversations",
                columns: new[] { "status", "assigned_to" });

            migrationBuilder.CreateIndex(
                name: "IX_conversations_window_expires_at",
                table: "conversations",
                column: "window_expires_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_messages_created_at_direction",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_messages_delivery_status",
                table: "messages");

            migrationBuilder.DropIndex(
                name: "IX_conversations_status_assigned_to",
                table: "conversations");

            migrationBuilder.DropIndex(
                name: "IX_conversations_window_expires_at",
                table: "conversations");
        }
    }
}
