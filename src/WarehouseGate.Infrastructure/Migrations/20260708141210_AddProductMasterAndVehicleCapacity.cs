using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProductMasterAndVehicleCapacity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                table: "Vehicles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthCm",
                table: "Vehicles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxWeightKg",
                table: "Vehicles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthCm",
                table: "Vehicles",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HeightCm",
                table: "VehicleMasters",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LengthCm",
                table: "VehicleMasters",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxWeightKg",
                table: "VehicleMasters",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "WidthCm",
                table: "VehicleMasters",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProductId",
                table: "DispatchOrderLines",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    WeightKg = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LengthCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WidthCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    HeightCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DispatchOrderLines_ProductId",
                table: "DispatchOrderLines",
                column: "ProductId");

            migrationBuilder.AddForeignKey(
                name: "FK_DispatchOrderLines_Products_ProductId",
                table: "DispatchOrderLines",
                column: "ProductId",
                principalTable: "Products",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DispatchOrderLines_Products_ProductId",
                table: "DispatchOrderLines");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropIndex(
                name: "IX_DispatchOrderLines_ProductId",
                table: "DispatchOrderLines");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "LengthCm",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "MaxWeightKg",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "HeightCm",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "LengthCm",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "MaxWeightKg",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "WidthCm",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "ProductId",
                table: "DispatchOrderLines");
        }
    }
}
