using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleExit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GateOutBySecurityUserId",
                table: "InwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GateOutTime",
                table: "InwardTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatePassToken",
                table: "InwardTransactions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GateOutBySecurityUserId",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "GateOutTime",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "GatePassToken",
                table: "InwardTransactions");
        }
    }
}
