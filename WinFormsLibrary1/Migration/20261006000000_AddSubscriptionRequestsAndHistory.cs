using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaElite.infrastructure.Migrations
{
    /// <summary>
    /// Subscription-request phase (OPTION 2): MASTER DB (db67598) ONLY - purely additive.
    /// No existing table, column, constraint or row is modified or dropped, so Companies /
    /// CompanyDatabase / Devices / Identity data are untouched.
    ///
    /// Added columns:
    ///   SubscriptionPlans     : Price, BillingCycle, TermsAndAgreement
    ///   CompanySubscriptions  : RenewalDate, Price, PaymentStatus, DateSubscribed, TermsAndAgreement
    ///     (Price / TermsAndAgreement are snapshots of the plan values at activation time;
    ///      existing rows receive neutral defaults and are never rewritten.)
    ///
    /// New tables:
    ///   SubscriptionHistory      - approved plan-change audit trail
    ///   SubscriptionChangeRequest - OPTION 2 tenant request / super-admin approval records
    ///
    /// The tenant-side gym member Subscriptions table lives in the tenant databases and is
    /// not touched by this migration. Seeding (Micro Business / Small Business) happens in
    /// code in a later phase, not here.
    /// </summary>
    public partial class AddSubscriptionRequestsAndHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1. New columns on SubscriptionPlans (defaults backfill existing rows) ----
            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "SubscriptionPlans",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                table: "SubscriptionPlans",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Monthly");

            migrationBuilder.AddColumn<string>(
                name: "TermsAndAgreement",
                table: "SubscriptionPlans",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // ---- 2. New columns on CompanySubscriptions (defaults backfill existing rows) ----
            migrationBuilder.AddColumn<DateTime>(
                name: "RenewalDate",
                table: "CompanySubscriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Price",
                table: "CompanySubscriptions",
                type: "decimal(18,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "PaymentStatus",
                table: "CompanySubscriptions",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "NotApplicable");

            migrationBuilder.AddColumn<DateTime>(
                name: "DateSubscribed",
                table: "CompanySubscriptions",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "TermsAndAgreement",
                table: "CompanySubscriptions",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            // ---- 3. SubscriptionHistory (audit of APPROVED plan changes) ----
            migrationBuilder.CreateTable(
                name: "SubscriptionHistory",
                columns: table => new
                {
                    HistoryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    PreviousPlanId = table.Column<int>(type: "int", nullable: true),
                    NewPlanId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    PreviousPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    NewPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionHistory", x => x.HistoryId);
                    table.ForeignKey(
                        name: "FK_SubscriptionHistory_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionHistory_SubscriptionPlans_NewPlanId",
                        column: x => x.NewPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "SubscriptionPlanId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionHistory_SubscriptionPlans_PreviousPlanId",
                        column: x => x.PreviousPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "SubscriptionPlanId",
                        onDelete: ReferentialAction.Restrict);
                });

            // ---- 4. SubscriptionChangeRequest (OPTION 2 request / approval records) ----
            migrationBuilder.CreateTable(
                name: "SubscriptionChangeRequest",
                columns: table => new
                {
                    SubscriptionChangeRequestId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    CurrentPlanId = table.Column<int>(type: "int", nullable: true),
                    RequestedPlanId = table.Column<int>(type: "int", nullable: true),
                    RequestType = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewNotes = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RequestedPrice = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionChangeRequest", x => x.SubscriptionChangeRequestId);
                    table.ForeignKey(
                        name: "FK_SubscriptionChangeRequest_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "CompanyId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionChangeRequest_SubscriptionPlans_CurrentPlanId",
                        column: x => x.CurrentPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "SubscriptionPlanId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionChangeRequest_SubscriptionPlans_RequestedPlanId",
                        column: x => x.RequestedPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "SubscriptionPlanId",
                        onDelete: ReferentialAction.Restrict);
                });

            // ---- 5. Indexes ----
            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionChangeRequest_CompanyId_Status",
                table: "SubscriptionChangeRequest",
                columns: new[] { "CompanyId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionChangeRequest_Status",
                table: "SubscriptionChangeRequest",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionChangeRequest_CurrentPlanId",
                table: "SubscriptionChangeRequest",
                column: "CurrentPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionChangeRequest_RequestedPlanId",
                table: "SubscriptionChangeRequest",
                column: "RequestedPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionHistory_CompanyId_ChangedAt",
                table: "SubscriptionHistory",
                columns: new[] { "CompanyId", "ChangedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionHistory_NewPlanId",
                table: "SubscriptionHistory",
                column: "NewPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionHistory_PreviousPlanId",
                table: "SubscriptionHistory",
                column: "PreviousPlanId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Only what this migration added is removed; nothing else in the master DB is touched.
            migrationBuilder.DropTable(
                name: "SubscriptionChangeRequest");

            migrationBuilder.DropTable(
                name: "SubscriptionHistory");

            migrationBuilder.DropColumn(
                name: "TermsAndAgreement",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "DateSubscribed",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "PaymentStatus",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "RenewalDate",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "TermsAndAgreement",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "BillingCycle",
                table: "SubscriptionPlans");

            migrationBuilder.DropColumn(
                name: "Price",
                table: "SubscriptionPlans");
        }
    }
}
