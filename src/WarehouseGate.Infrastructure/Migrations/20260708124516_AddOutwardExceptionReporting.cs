using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutwardExceptionReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExceptionReason",
                table: "OutwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExceptionRemarks",
                table: "OutwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExceptionReportedAt",
                table: "OutwardTransactions",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExceptionReason",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "ExceptionRemarks",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "ExceptionReportedAt",
                table: "OutwardTransactions");
        }
    }
}
