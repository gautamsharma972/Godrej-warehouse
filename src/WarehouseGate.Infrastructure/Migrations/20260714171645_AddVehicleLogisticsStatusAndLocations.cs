using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleLogisticsStatusAndLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FromLocation",
                table: "VehicleLogisticsRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "VehicleLogisticsRecords",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "InTransit");

            migrationBuilder.AddColumn<string>(
                name: "ToLocation",
                table: "VehicleLogisticsRecords",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_Status",
                table: "VehicleLogisticsRecords",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehicleLogisticsRecords_Status",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropColumn(
                name: "FromLocation",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropColumn(
                name: "ToLocation",
                table: "VehicleLogisticsRecords");
        }
    }
}
