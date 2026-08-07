using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUserProfileFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "birth_date",
                table: "users",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contract_document_file_name",
                table: "users",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contract_document_path",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "users",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "users",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "address",
                table: "users");

            migrationBuilder.DropColumn(
                name: "birth_date",
                table: "users");

            migrationBuilder.DropColumn(
                name: "contract_document_file_name",
                table: "users");

            migrationBuilder.DropColumn(
                name: "contract_document_path",
                table: "users");

            migrationBuilder.DropColumn(
                name: "email",
                table: "users");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "users");
        }
    }
}
