using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDispatchOrderLineIdToOutwardPhotoEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DispatchOrderLineId",
                table: "OutwardPhotoEvidences",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutwardPhotoEvidences_DispatchOrderLineId",
                table: "OutwardPhotoEvidences",
                column: "DispatchOrderLineId");

            migrationBuilder.AddForeignKey(
                name: "FK_OutwardPhotoEvidences_DispatchOrderLines_DispatchOrderLineId",
                table: "OutwardPhotoEvidences",
                column: "DispatchOrderLineId",
                principalTable: "DispatchOrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OutwardPhotoEvidences_DispatchOrderLines_DispatchOrderLineId",
                table: "OutwardPhotoEvidences");

            migrationBuilder.DropIndex(
                name: "IX_OutwardPhotoEvidences_DispatchOrderLineId",
                table: "OutwardPhotoEvidences");

            migrationBuilder.DropColumn(
                name: "DispatchOrderLineId",
                table: "OutwardPhotoEvidences");
        }
    }
}
