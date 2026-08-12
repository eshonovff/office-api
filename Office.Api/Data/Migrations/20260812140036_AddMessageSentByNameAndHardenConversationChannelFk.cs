using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageSentByNameAndHardenConversationChannelFk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_channels_channel_id",
                table: "conversations");

            migrationBuilder.AddColumn<string>(
                name: "sent_by_user_name",
                table: "messages",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Backfill: existing rows only ever got a name via SentByUser at read time —
            // snapshot it now from whoever the FK still points to. Rows whose sender was
            // already deleted (sent_by_user_id already SET NULL) stay unattributed; there's
            // no way to recover a name once that link is gone.
            migrationBuilder.Sql("""
                UPDATE messages
                SET sent_by_user_name = users.full_name
                FROM users
                WHERE messages.sent_by_user_id = users.id
                  AND messages.sent_by_user_name IS NULL;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_channels_channel_id",
                table: "conversations",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_conversations_channels_channel_id",
                table: "conversations");

            migrationBuilder.DropColumn(
                name: "sent_by_user_name",
                table: "messages");

            migrationBuilder.AddForeignKey(
                name: "FK_conversations_channels_channel_id",
                table: "conversations",
                column: "channel_id",
                principalTable: "channels",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
