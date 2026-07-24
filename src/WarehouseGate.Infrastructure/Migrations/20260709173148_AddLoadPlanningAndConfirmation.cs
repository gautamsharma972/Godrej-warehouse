using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLoadPlanningAndConfirmation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActualLoadingStartedAt",
                table: "OutwardTransactions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OutwardLoadPlanGroupId",
                table: "OutwardPhotoEvidences",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutwardLoadPlanOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsSelected = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardLoadPlanOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardLoadPlanOptions_OutwardTransactions_OutwardTransactionId",
                        column: x => x.OutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OutwardLoadPlanGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OutwardLoadPlanOptionId = table.Column<int>(type: "int", nullable: false),
                    DispatchOrderLineId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ZoneLength = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZoneWidth = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ZoneHeight = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PositionXCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PositionYCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PositionZCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DimXCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DimYCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DimZCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Orientation = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Columns = table.Column<int>(type: "int", nullable: false),
                    Layers = table.Column<int>(type: "int", nullable: false),
                    LoadSequence = table.Column<int>(type: "int", nullable: false),
                    Color = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ConfirmationStatus = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ActualQuantity = table.Column<int>(type: "int", nullable: true),
                    ActualNotes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ConfirmedByUserId = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardLoadPlanGroups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardLoadPlanGroups_DispatchOrderLines_DispatchOrderLineId",
                        column: x => x.DispatchOrderLineId,
                        principalTable: "DispatchOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardLoadPlanGroups_OutwardLoadPlanOptions_OutwardLoadPlanOptionId",
                        column: x => x.OutwardLoadPlanOptionId,
                        principalTable: "OutwardLoadPlanOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutwardPhotoEvidences_OutwardLoadPlanGroupId",
                table: "OutwardPhotoEvidences",
                column: "OutwardLoadPlanGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardLoadPlanGroups_DispatchOrderLineId",
                table: "OutwardLoadPlanGroups",
                column: "DispatchOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardLoadPlanGroups_OutwardLoadPlanOptionId",
                table: "OutwardLoadPlanGroups",
                column: "OutwardLoadPlanOptionId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardLoadPlanOptions_OutwardTransactionId",
                table: "OutwardLoadPlanOptions",
                column: "OutwardTransactionId");

            migrationBuilder.AddForeignKey(
                name: "FK_OutwardPhotoEvidences_OutwardLoadPlanGroups_OutwardLoadPlanGroupId",
                table: "OutwardPhotoEvidences",
                column: "OutwardLoadPlanGroupId",
                principalTable: "OutwardLoadPlanGroups",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OutwardPhotoEvidences_OutwardLoadPlanGroups_OutwardLoadPlanGroupId",
                table: "OutwardPhotoEvidences");

            migrationBuilder.DropTable(
                name: "OutwardLoadPlanGroups");

            migrationBuilder.DropTable(
                name: "OutwardLoadPlanOptions");

            migrationBuilder.DropIndex(
                name: "IX_OutwardPhotoEvidences_OutwardLoadPlanGroupId",
                table: "OutwardPhotoEvidences");

            migrationBuilder.DropColumn(
                name: "ActualLoadingStartedAt",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "OutwardLoadPlanGroupId",
                table: "OutwardPhotoEvidences");
        }
    }
}
