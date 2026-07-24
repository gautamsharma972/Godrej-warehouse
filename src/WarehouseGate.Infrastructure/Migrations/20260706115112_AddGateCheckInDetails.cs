using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGateCheckInDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ExpectedDeliveryDate",
                table: "PurchaseOrders",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverMobile",
                table: "InwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "InwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GateName",
                table: "InwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GpsLatitude",
                table: "InwardTransactions",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GpsLongitude",
                table: "InwardTransactions",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasDeliveryDateMismatch",
                table: "InwardTransactions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsNewVehicle",
                table: "InwardTransactions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "TransporterName",
                table: "InwardTransactions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InwardDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InwardDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InwardDocuments_InwardTransactions_InwardTransactionId",
                        column: x => x.InwardTransactionId,
                        principalTable: "InwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InwardDocuments_InwardTransactionId",
                table: "InwardDocuments",
                column: "InwardTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InwardDocuments");

            migrationBuilder.DropColumn(
                name: "ExpectedDeliveryDate",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "DriverMobile",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "DriverName",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "GateName",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "GpsLatitude",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "GpsLongitude",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "HasDeliveryDateMismatch",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "IsNewVehicle",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "TransporterName",
                table: "InwardTransactions");
        }
    }
}
