using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Office.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "plan_expires_at",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "plan_tier",
                table: "customers",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "trial_ends_at",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            // Customers verified before this migration get the same 7-day trial, counted from
            // their verification. Unverified ones get it on verify (StartTrialIfNotStarted).
            migrationBuilder.Sql(
                "UPDATE customers SET trial_ends_at = email_verified_at + interval '7 days' " +
                "WHERE email_verified_at IS NOT NULL AND trial_ends_at IS NULL;");

            migrationBuilder.CreateTable(
                name: "subscription_requests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    duration_months = table.Column<int>(type: "integer", nullable: false),
                    expected_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    receipt_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    receipt_file_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_by_user_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscription_requests", x => x.id);
                    table.ForeignKey(
                        name: "FK_subscription_requests_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_subscription_requests_users_reviewed_by_user_id",
                        column: x => x.reviewed_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_requests_customer_id_status",
                table: "subscription_requests",
                columns: new[] { "customer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_subscription_requests_reviewed_by_user_id",
                table: "subscription_requests",
                column: "reviewed_by_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_subscription_requests_status_submitted_at",
                table: "subscription_requests",
                columns: new[] { "status", "submitted_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "subscription_requests");

            migrationBuilder.DropColumn(
                name: "plan_expires_at",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "plan_tier",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "trial_ends_at",
                table: "customers");
        }
    }
}
