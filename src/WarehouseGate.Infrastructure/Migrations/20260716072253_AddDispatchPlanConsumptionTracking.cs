using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchPlanConsumptionTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ConsumedByInwardTransactionId",
                table: "VehicleLogisticsRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConsumedByOutwardTransactionId",
                table: "VehicleLogisticsRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SkuCode",
                table: "Products",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_ConsumedByInwardTransactionId",
                table: "VehicleLogisticsRecords",
                column: "ConsumedByInwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_ConsumedByOutwardTransactionId",
                table: "VehicleLogisticsRecords",
                column: "ConsumedByOutwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_SkuCode",
                table: "Products",
                column: "SkuCode",
                unique: true,
                filter: "[SkuCode] <> ''");

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleLogisticsRecords_InwardTransactions_ConsumedByInwardTransactionId",
                table: "VehicleLogisticsRecords",
                column: "ConsumedByInwardTransactionId",
                principalTable: "InwardTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleLogisticsRecords_OutwardTransactions_ConsumedByOutwardTransactionId",
                table: "VehicleLogisticsRecords",
                column: "ConsumedByOutwardTransactionId",
                principalTable: "OutwardTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleLogisticsRecords_InwardTransactions_ConsumedByInwardTransactionId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleLogisticsRecords_OutwardTransactions_ConsumedByOutwardTransactionId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropIndex(
                name: "IX_VehicleLogisticsRecords_ConsumedByInwardTransactionId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropIndex(
                name: "IX_VehicleLogisticsRecords_ConsumedByOutwardTransactionId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropIndex(
                name: "IX_Products_SkuCode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ConsumedByInwardTransactionId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropColumn(
                name: "ConsumedByOutwardTransactionId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.AlterColumn<string>(
                name: "SkuCode",
                table: "Products",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
