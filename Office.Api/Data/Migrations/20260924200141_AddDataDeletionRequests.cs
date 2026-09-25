using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDataDeletionRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "data_deletion_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    meta_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    confirmation_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    signed_request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    channels_deleted = table.Column<int>(type: "integer", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_data_deletion_requests", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_data_deletion_requests_confirmation_code",
                table: "data_deletion_requests",
                column: "confirmation_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_data_deletion_requests_signed_request_hash",
                table: "data_deletion_requests",
                column: "signed_request_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "data_deletion_requests");
        }
    }
}
