using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutwardGateTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriverMobile",
                table: "OutwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "OutwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateInBySecurityUserId",
                table: "OutwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GateInTime",
                table: "OutwardTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateName",
                table: "OutwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateOutBySecurityUserId",
                table: "OutwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GateOutTime",
                table: "OutwardTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GatePassToken",
                table: "OutwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GpsLatitude",
                table: "OutwardTransactions",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GpsLongitude",
                table: "OutwardTransactions",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransporterName",
                table: "OutwardTransactions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DriverMobile",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "DriverName",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "GateInBySecurityUserId",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "GateInTime",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "GateName",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "GateOutBySecurityUserId",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "GateOutTime",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "GatePassToken",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "GpsLatitude",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "GpsLongitude",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "TransporterName",
                table: "OutwardTransactions");
        }
    }
}
