using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaElite.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialKeyToCompanyDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CredentialKey",
                table: "CompanyDatabase",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CredentialKey",
                table: "CompanyDatabase");
        }
    }
}
