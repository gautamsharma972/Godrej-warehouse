using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFromToWarehouseToVehicleLogisticsRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleLogisticsRecords_Warehouses_WarehouseId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropColumn(
                name: "FromLocation",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropColumn(
                name: "ToLocation",
                table: "VehicleLogisticsRecords");

            migrationBuilder.RenameColumn(
                name: "WarehouseId",
                table: "VehicleLogisticsRecords",
                newName: "ToWarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_VehicleLogisticsRecords_WarehouseId",
                table: "VehicleLogisticsRecords",
                newName: "IX_VehicleLogisticsRecords_ToWarehouseId");

            migrationBuilder.AddColumn<int>(
                name: "FromWarehouseId",
                table: "VehicleLogisticsRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_FromWarehouseId",
                table: "VehicleLogisticsRecords",
                column: "FromWarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleLogisticsRecords_Warehouses_FromWarehouseId",
                table: "VehicleLogisticsRecords",
                column: "FromWarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleLogisticsRecords_Warehouses_ToWarehouseId",
                table: "VehicleLogisticsRecords",
                column: "ToWarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleLogisticsRecords_Warehouses_FromWarehouseId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleLogisticsRecords_Warehouses_ToWarehouseId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropIndex(
                name: "IX_VehicleLogisticsRecords_FromWarehouseId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropColumn(
                name: "FromWarehouseId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.RenameColumn(
                name: "ToWarehouseId",
                table: "VehicleLogisticsRecords",
                newName: "WarehouseId");

            migrationBuilder.RenameIndex(
                name: "IX_VehicleLogisticsRecords_ToWarehouseId",
                table: "VehicleLogisticsRecords",
                newName: "IX_VehicleLogisticsRecords_WarehouseId");

            migrationBuilder.AddColumn<string>(
                name: "FromLocation",
                table: "VehicleLogisticsRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToLocation",
                table: "VehicleLogisticsRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleLogisticsRecords_Warehouses_WarehouseId",
                table: "VehicleLogisticsRecords",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
