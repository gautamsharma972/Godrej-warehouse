using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOutwardFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DispatchOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DispatchOrderNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RequestedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchOrders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DispatchOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DispatchOrderId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OrderedQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchOrderLines_DispatchOrders_DispatchOrderId",
                        column: x => x.DispatchOrderId,
                        principalTable: "DispatchOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutwardTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DispatchOrderId = table.Column<int>(type: "int", nullable: false),
                    OutwardTxnNumber = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedTime = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByOfficeUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AssignedSupervisorUserId = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AssignedTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    VehicleId = table.Column<int>(type: "int", nullable: true),
                    BayName = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DockInTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DockOutTime = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardTransactions_DispatchOrders_DispatchOrderId",
                        column: x => x.DispatchOrderId,
                        principalTable: "DispatchOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardTransactions_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutwardDispatchNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    DispatchNoteNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsPartial = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardDispatchNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardDispatchNotes_OutwardTransactions_OutwardTransactionId",
                        column: x => x.OutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutwardLoadLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    DispatchOrderLineId = table.Column<int>(type: "int", nullable: false),
                    LoadedQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LoadSequence = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardLoadLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardLoadLines_DispatchOrderLines_DispatchOrderLineId",
                        column: x => x.DispatchOrderLineId,
                        principalTable: "DispatchOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutwardLoadLines_OutwardTransactions_OutwardTransactionId",
                        column: x => x.OutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OutwardPhotoEvidences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FilePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardPhotoEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardPhotoEvidences_OutwardTransactions_OutwardTransactionId",
                        column: x => x.OutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchOrderLines_DispatchOrderId",
                table: "DispatchOrderLines",
                column: "DispatchOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchOrders_DispatchOrderNumber",
                table: "DispatchOrders",
                column: "DispatchOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutwardDispatchNotes_OutwardTransactionId",
                table: "OutwardDispatchNotes",
                column: "OutwardTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutwardLoadLines_DispatchOrderLineId",
                table: "OutwardLoadLines",
                column: "DispatchOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardLoadLines_OutwardTransactionId",
                table: "OutwardLoadLines",
                column: "OutwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardPhotoEvidences_OutwardTransactionId",
                table: "OutwardPhotoEvidences",
                column: "OutwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardTransactions_DispatchOrderId",
                table: "OutwardTransactions",
                column: "DispatchOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardTransactions_OutwardTxnNumber",
                table: "OutwardTransactions",
                column: "OutwardTxnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutwardTransactions_VehicleId",
                table: "OutwardTransactions",
                column: "VehicleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutwardDispatchNotes");

            migrationBuilder.DropTable(
                name: "OutwardLoadLines");

            migrationBuilder.DropTable(
                name: "OutwardPhotoEvidences");

            migrationBuilder.DropTable(
                name: "DispatchOrderLines");

            migrationBuilder.DropTable(
                name: "OutwardTransactions");

            migrationBuilder.DropTable(
                name: "DispatchOrders");
        }
    }
}
