using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelMetaAppScopedUserId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "meta_app_scoped_user_id",
                table: "channels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_channels_meta_app_scoped_user_id",
                table: "channels",
                column: "meta_app_scoped_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_channels_meta_app_scoped_user_id",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "meta_app_scoped_user_id",
                table: "channels");
        }
    }
}
