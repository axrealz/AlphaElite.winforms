using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaElite.infrastructure.Migrations.TenantCAlphaEliteDb
{
    /// <summary>
    /// Phase 8 (approved): branch management additions. TENANT C (db68417) ONLY.
    ///
    /// Additive, nullable schema change — no existing column is modified or dropped:
    ///   Branches:      + City (nvarchar(100)), + ManagerUserAccountId (int, FK UserAccounts NO ACTION),
    ///                  + UpdatedAt (datetime2)
    ///   Subscriptions: + BranchId (int, FK Branches NO ACTION)
    ///
    /// Data changes performed here (approved in the Phase 8A audit):
    ///   1. Backfill Subscriptions.BranchId from the member's home branch.
    ///   2. Rename in place: BR-A -> BR-MAIN (Manila), BR-B -> BR-CEBU (Cebu).
    ///   3. Insert BR-DAVAO (empty, Active).
    ///   4. Assign the existing Manager account (UserAccountId 2) as manager of BR-MAIN.
    ///   5. Data hygiene: UserAccounts.BranchId = NULL for the tenant-wide Admin row.
    /// No transactional BranchId is rewritten; historical rows keep their original branch.
    /// </summary>
    public partial class AddBranchManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- schema: Branches ----
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Branches",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManagerUserAccountId",
                table: "Branches",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAt",
                table: "Branches",
                type: "datetime2",
                nullable: true);

            // ---- schema: Subscriptions ----
            migrationBuilder.AddColumn<int>(
                name: "BranchId",
                table: "Subscriptions",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Subscriptions_BranchId",
                table: "Subscriptions",
                column: "BranchId");

            migrationBuilder.AddForeignKey(
                name: "FK_Subscriptions_Branches_BranchId",
                table: "Subscriptions",
                column: "BranchId",
                principalTable: "Branches",
                principalColumn: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_Branches_ManagerUserAccountId",
                table: "Branches",
                column: "ManagerUserAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_Branches_UserAccounts_ManagerUserAccountId",
                table: "Branches",
                column: "ManagerUserAccountId",
                principalTable: "UserAccounts",
                principalColumn: "UserAccountId");

            // ---- data (approved Phase 8A mapping) ----
            // 1. Subscriptions follow the member's home branch.
            migrationBuilder.Sql(
                "UPDATE s SET s.BranchId = m.BranchId " +
                "FROM dbo.Subscriptions s INNER JOIN dbo.Members m ON s.MemberId = m.MemberId " +
                "WHERE s.BranchId IS NULL AND m.BranchId IS NOT NULL;");

            // 2. Rename in place — BranchIds 1 and 2 are preserved so every existing FK stays valid.
            migrationBuilder.Sql(
                "UPDATE dbo.Branches SET BranchCode = N'BR-MAIN', BranchName = N'AlphaElite Manila - Head Office', " +
                "City = N'Manila City', ManagerUserAccountId = 2, UpdatedAt = SYSUTCDATETIME() " +
                "WHERE BranchId = 1 AND BranchCode = N'BR-A';");

            migrationBuilder.Sql(
                "UPDATE dbo.Branches SET BranchCode = N'BR-CEBU', BranchName = N'AlphaElite Cebu Branch', " +
                "City = N'Cebu City', UpdatedAt = SYSUTCDATETIME() " +
                "WHERE BranchId = 2 AND BranchCode = N'BR-B';");

            // 3. New empty branch (idempotent: only when the code does not exist yet).
            migrationBuilder.Sql(
                "IF NOT EXISTS (SELECT 1 FROM dbo.Branches WHERE BranchCode = N'BR-DAVAO') " +
                "INSERT INTO dbo.Branches (BranchCode, BranchName, City, IsActive, CreatedAt) " +
                "VALUES (N'BR-DAVAO', N'AlphaElite Davao Branch', N'Davao City', 1, SYSUTCDATETIME());");

            // 4. Data hygiene: Admin is tenant-wide; its carry-over BranchId is not an authorization input.
            migrationBuilder.Sql(
                "UPDATE dbo.UserAccounts SET BranchId = NULL WHERE Username = N'admin' AND Role = N'Admin' AND BranchId IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert the data changes (rename back, remove DAVAO, unassign manager, refill admin branch).
            migrationBuilder.Sql(
                "UPDATE dbo.UserAccounts SET BranchId = 2 WHERE Username = N'admin' AND Role = N'Admin' AND BranchId IS NULL;");

            migrationBuilder.Sql(
                "UPDATE dbo.Branches SET ManagerUserAccountId = NULL WHERE BranchId = 1;");

            migrationBuilder.Sql(
                "DELETE FROM dbo.Branches WHERE BranchCode = N'BR-DAVAO' AND NOT EXISTS (SELECT 1 FROM dbo.Members WHERE BranchId = Branches.BranchId);");

            migrationBuilder.Sql(
                "UPDATE dbo.Branches SET BranchCode = N'BR-B', BranchName = N'Branch B', City = NULL, UpdatedAt = SYSUTCDATETIME() WHERE BranchId = 2 AND BranchCode = N'BR-CEBU';");

            migrationBuilder.Sql(
                "UPDATE dbo.Branches SET BranchCode = N'BR-A', BranchName = N'Branch A', City = NULL, UpdatedAt = SYSUTCDATETIME() WHERE BranchId = 1 AND BranchCode = N'BR-MAIN';");

            migrationBuilder.Sql(
                "UPDATE s SET s.BranchId = NULL FROM dbo.Subscriptions s WHERE s.BranchId IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Branches_UserAccounts_ManagerUserAccountId",
                table: "Branches");

            migrationBuilder.DropIndex(
                name: "IX_Branches_ManagerUserAccountId",
                table: "Branches");

            migrationBuilder.DropForeignKey(
                name: "FK_Subscriptions_Branches_BranchId",
                table: "Subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_Subscriptions_BranchId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "BranchId",
                table: "Subscriptions");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "ManagerUserAccountId",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Branches");
        }
    }
}
