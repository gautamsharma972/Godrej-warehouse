using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutwardGateArrival : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutwardGateArrivals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    GateInTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GateInBySecurityUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DriverName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DriverMobile = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TransporterName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GateName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GpsLatitude = table.Column<double>(type: "float", nullable: true),
                    GpsLongitude = table.Column<double>(type: "float", nullable: true),
                    SecurityEnteredDispatchOrderNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LinkedOutwardTransactionId = table.Column<int>(type: "int", nullable: true),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LinkedByOfficeUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardGateArrivals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivals_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivals_OutwardTransactions_LinkedOutwardTransactionId",
                        column: x => x.LinkedOutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivals_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivals_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OutwardGateArrivalPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutwardGateArrivalId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardGateArrivalPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivalPhotos_OutwardGateArrivals_OutwardGateArrivalId",
                        column: x => x.OutwardGateArrivalId,
                        principalTable: "OutwardGateArrivals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivalPhotos_OutwardGateArrivalId",
                table: "OutwardGateArrivalPhotos",
                column: "OutwardGateArrivalId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivals_LinkedOutwardTransactionId",
                table: "OutwardGateArrivals",
                column: "LinkedOutwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivals_OrganizationId",
                table: "OutwardGateArrivals",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivals_VehicleId",
                table: "OutwardGateArrivals",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivals_WarehouseId",
                table: "OutwardGateArrivals",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutwardGateArrivalPhotos");

            migrationBuilder.DropTable(
                name: "OutwardGateArrivals");
        }
    }
}
