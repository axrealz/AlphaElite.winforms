using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaElite.infrastructure.Migrations
{
    /// <summary>
    /// Plan-configuration versioning (MASTER DB db67598 ONLY) - purely additive.
    ///
    /// No existing table, column, constraint or row is modified or dropped, so Companies /
    /// CompanyDatabase / Devices / Identity / SubscriptionPlans / SoftwareModules / PlanModules /
    /// CompanySubscriptions / SubscriptionHistory / SubscriptionChangeRequest data is untouched.
    /// Tenant databases are not touched by this migration at all.
    ///
    /// New tables:
    ///   SubscriptionPlanVersions        - immutable per-plan configuration snapshot (name, price,
    ///                                     billing cycle, Terms &amp; Agreement, effective date,
    ///                                     current flag, change type/summary, changed by/at).
    ///                                     Unique (SubscriptionPlanId, VersionNumber) - version
    ///                                     numbers are sequential PER PLAN.
    ///   SubscriptionPlanVersionModules  - the IMMUTABLE module set of one version (composite key
    ///                                     version + module, with the module code/name snapshotted).
    ///                                     Historical module membership is stored here so an old
    ///                                     version is never reconstructed from PlanModules.
    ///
    /// New nullable column:
    ///   CompanySubscriptions.SubscriptionPlanVersionId - the plan configuration version a company
    ///   received. Nullable and additive: existing subscriptions keep working and are linked by the
    ///   idempotent version initializer (version rows are never deleted while referenced; the
    ///   pointer is simply cleared - ON DELETE SET NULL - if a version ever disappears with its
    ///   plan). The subscription's own Price / TermsAndAgreement / DateSubscribed / RenewalDate /
    ///   PaymentStatus snapshots are NOT changed by this migration.
    /// </summary>
    public partial class AddPlanVersioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1. SubscriptionPlanVersions (immutable snapshots, one row per plan version) ----
            migrationBuilder.CreateTable(
                name: "SubscriptionPlanVersions",
                columns: table => new
                {
                    SubscriptionPlanVersionId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SubscriptionPlanId = table.Column<int>(type: "int", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    PlanCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PlanName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    BillingCycle = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TermsAndAgreement = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PlanIsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsCurrent = table.Column<bool>(type: "bit", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ChangeSummary = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ChangedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanVersions", x => x.SubscriptionPlanVersionId);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanVersions_SubscriptionPlans_SubscriptionPlanId",
                        column: x => x.SubscriptionPlanId,
                        principalTable: "SubscriptionPlans",
                        principalColumn: "SubscriptionPlanId",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- 2. SubscriptionPlanVersionModules (the immutable module set of a version) ----
            migrationBuilder.CreateTable(
                name: "SubscriptionPlanVersionModules",
                columns: table => new
                {
                    SubscriptionPlanVersionId = table.Column<int>(type: "int", nullable: false),
                    ModuleId = table.Column<int>(type: "int", nullable: false),
                    ModuleCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ModuleName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubscriptionPlanVersionModules", x => new { x.SubscriptionPlanVersionId, x.ModuleId });
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanVersionModules_SoftwareModules_ModuleId",
                        column: x => x.ModuleId,
                        principalTable: "SoftwareModules",
                        principalColumn: "ModuleId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubscriptionPlanVersionModules_SubscriptionPlanVersions_SubscriptionPlanVersionId",
                        column: x => x.SubscriptionPlanVersionId,
                        principalTable: "SubscriptionPlanVersions",
                        principalColumn: "SubscriptionPlanVersionId",
                        onDelete: ReferentialAction.Cascade);
                });

            // ---- 3. CompanySubscriptions -> plan version association (nullable, additive) ----
            migrationBuilder.AddColumn<int>(
                name: "SubscriptionPlanVersionId",
                table: "CompanySubscriptions",
                type: "int",
                nullable: true);

            // ---- 4. Indexes ----
            migrationBuilder.CreateIndex(
                name: "IX_CompanySubscriptions_SubscriptionPlanVersionId",
                table: "CompanySubscriptions",
                column: "SubscriptionPlanVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanVersionModules_ModuleId",
                table: "SubscriptionPlanVersionModules",
                column: "ModuleId");

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanVersions_SubscriptionPlanId_IsCurrent",
                table: "SubscriptionPlanVersions",
                columns: new[] { "SubscriptionPlanId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_SubscriptionPlanVersions_SubscriptionPlanId_VersionNumber",
                table: "SubscriptionPlanVersions",
                columns: new[] { "SubscriptionPlanId", "VersionNumber" },
                unique: true);

            // ---- 5. CompanySubscriptions -> SubscriptionPlanVersions foreign key ----
            migrationBuilder.AddForeignKey(
                name: "FK_CompanySubscriptions_SubscriptionPlanVersions_SubscriptionPlanVersionId",
                table: "CompanySubscriptions",
                column: "SubscriptionPlanVersionId",
                principalTable: "SubscriptionPlanVersions",
                principalColumn: "SubscriptionPlanVersionId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rollback of THIS migration only: it removes exactly what it created. No plan,
            // module, mapping, subscription or audit row is deleted by it.
            migrationBuilder.DropForeignKey(
                name: "FK_CompanySubscriptions_SubscriptionPlanVersions_SubscriptionPlanVersionId",
                table: "CompanySubscriptions");

            migrationBuilder.DropIndex(
                name: "IX_CompanySubscriptions_SubscriptionPlanVersionId",
                table: "CompanySubscriptions");

            migrationBuilder.DropColumn(
                name: "SubscriptionPlanVersionId",
                table: "CompanySubscriptions");

            migrationBuilder.DropTable(
                name: "SubscriptionPlanVersionModules");

            migrationBuilder.DropTable(
                name: "SubscriptionPlanVersions");
        }
    }
}
