using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMessageExternalContentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_content_kind",
                table: "messages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_content_url",
                table: "messages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "external_content_kind",
                table: "messages");

            migrationBuilder.DropColumn(
                name: "external_content_url",
                table: "messages");
        }
    }
}
