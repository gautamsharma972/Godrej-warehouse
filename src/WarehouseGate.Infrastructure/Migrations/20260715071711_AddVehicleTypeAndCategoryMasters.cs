using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleTypeAndCategoryMasters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VehicleMasters_VehicleNumber",
                table: "VehicleMasters");

            migrationBuilder.Sql("DELETE FROM [VehicleMasters]");

            migrationBuilder.DropColumn(
                name: "DriverMobile",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "DriverName",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "TransporterName",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "VehicleNumber",
                table: "VehicleMasters");

            migrationBuilder.AddColumn<int>(
                name: "VehicleCategoryId",
                table: "VehicleMasters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VehicleTypeId",
                table: "VehicleMasters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "VehicleCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(450)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMasters_VehicleCategoryId",
                table: "VehicleMasters",
                column: "VehicleCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMasters_VehicleTypeId_VehicleCategoryId",
                table: "VehicleMasters",
                columns: new[] { "VehicleTypeId", "VehicleCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCategories_Name",
                table: "VehicleCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTypes_Name",
                table: "VehicleTypes",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleMasters_VehicleCategories_VehicleCategoryId",
                table: "VehicleMasters",
                column: "VehicleCategoryId",
                principalTable: "VehicleCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleMasters_VehicleTypes_VehicleTypeId",
                table: "VehicleMasters",
                column: "VehicleTypeId",
                principalTable: "VehicleTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_VehicleMasters_VehicleCategories_VehicleCategoryId",
                table: "VehicleMasters");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleMasters_VehicleTypes_VehicleTypeId",
                table: "VehicleMasters");

            migrationBuilder.DropTable(
                name: "VehicleCategories");

            migrationBuilder.DropTable(
                name: "VehicleTypes");

            migrationBuilder.DropIndex(
                name: "IX_VehicleMasters_VehicleCategoryId",
                table: "VehicleMasters");

            migrationBuilder.DropIndex(
                name: "IX_VehicleMasters_VehicleTypeId_VehicleCategoryId",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "VehicleCategoryId",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "VehicleTypeId",
                table: "VehicleMasters");

            migrationBuilder.AddColumn<string>(
                name: "DriverMobile",
                table: "VehicleMasters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DriverName",
                table: "VehicleMasters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransporterName",
                table: "VehicleMasters",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VehicleNumber",
                table: "VehicleMasters",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMasters_VehicleNumber",
                table: "VehicleMasters",
                column: "VehicleNumber",
                unique: true);
        }
    }
}
