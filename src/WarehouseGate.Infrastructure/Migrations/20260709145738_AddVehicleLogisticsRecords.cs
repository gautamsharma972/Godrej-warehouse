using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleLogisticsRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VehicleLogisticsRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VehicleNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    PoNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InwardTransactionId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransporterName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DriverPhone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VehicleType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Sku = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SkuCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BoxQuantity = table.Column<int>(type: "int", nullable: false),
                    DepartureDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EtaDateTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleLogisticsRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleLogisticsRecords_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_VehicleNumber",
                table: "VehicleLogisticsRecords",
                column: "VehicleNumber");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_WarehouseId",
                table: "VehicleLogisticsRecords",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleLogisticsRecords");
        }
    }
}
