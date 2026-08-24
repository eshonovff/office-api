using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConversationContactUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "contact_username",
                table: "conversations",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "contact_username",
                table: "conversations");
        }
    }
}
