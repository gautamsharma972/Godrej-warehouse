using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InwardWorkflowOverhaul : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "VehicleLogisticsRecords",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderLineId",
                table: "PhotoEvidences",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UnplannedReceiptLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UnplannedReceiptLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UnplannedReceiptLines_InwardTransactions_InwardTransactionId",
                        column: x => x.InwardTransactionId,
                        principalTable: "InwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UnplannedReceiptLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PhotoEvidences_PurchaseOrderLineId",
                table: "PhotoEvidences",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_UnplannedReceiptLines_InwardTransactionId",
                table: "UnplannedReceiptLines",
                column: "InwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_UnplannedReceiptLines_ProductId",
                table: "UnplannedReceiptLines",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_PhotoEvidences_PurchaseOrderLines_PurchaseOrderLineId",
                table: "PhotoEvidences",
                column: "PurchaseOrderLineId",
                principalTable: "PurchaseOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PhotoEvidences_PurchaseOrderLines_PurchaseOrderLineId",
                table: "PhotoEvidences");

            migrationBuilder.DropTable(
                name: "UnplannedReceiptLines");

            migrationBuilder.DropIndex(
                name: "IX_PhotoEvidences_PurchaseOrderLineId",
                table: "PhotoEvidences");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderLineId",
                table: "PhotoEvidences");

            migrationBuilder.AlterColumn<string>(
                name: "VehicleNumber",
                table: "VehicleLogisticsRecords",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);
        }
    }
}
