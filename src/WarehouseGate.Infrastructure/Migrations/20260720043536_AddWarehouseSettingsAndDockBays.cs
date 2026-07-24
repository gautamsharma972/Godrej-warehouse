using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseSettingsAndDockBays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DockOperatingHoursPerDay",
                table: "Warehouses",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ShiftHoursPerDay",
                table: "Warehouses",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SlaTargetMinutes",
                table: "Warehouses",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DockBays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DockBays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DockBays_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DockBays_WarehouseId_Name",
                table: "DockBays",
                columns: new[] { "WarehouseId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DockBays");

            migrationBuilder.DropColumn(
                name: "DockOperatingHoursPerDay",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "ShiftHoursPerDay",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "SlaTargetMinutes",
                table: "Warehouses");
        }
    }
}
