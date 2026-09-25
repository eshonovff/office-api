using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChannelCustomerOwner : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "customer_id",
                table: "channels",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_channels_customer_id",
                table: "channels",
                column: "customer_id");

            migrationBuilder.AddForeignKey(
                name: "FK_channels_customers_customer_id",
                table: "channels",
                column: "customer_id",
                principalTable: "customers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_channels_customers_customer_id",
                table: "channels");

            migrationBuilder.DropIndex(
                name: "IX_channels_customer_id",
                table: "channels");

            migrationBuilder.DropColumn(
                name: "customer_id",
                table: "channels");
        }
    }
}
