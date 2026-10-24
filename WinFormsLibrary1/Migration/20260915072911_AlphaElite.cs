using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlphaElite.infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AlphaElite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Devices_Devices_DeviceId1",
                table: "Devices");

            migrationBuilder.DropIndex(
                name: "IX_Devices_DeviceId1",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "DeviceId1",
                table: "Devices");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeviceId1",
                table: "Devices",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Devices_DeviceId1",
                table: "Devices",
                column: "DeviceId1");

            migrationBuilder.AddForeignKey(
                name: "FK_Devices_Devices_DeviceId1",
                table: "Devices",
                column: "DeviceId1",
                principalTable: "Devices",
                principalColumn: "DeviceId");
        }
    }
}
