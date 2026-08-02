using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Warehouses_Name",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTypes_Name",
                table: "VehicleTypes");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_Number",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_VehicleMasters_VehicleTypeId_VehicleCategoryId",
                table: "VehicleMasters");

            migrationBuilder.DropIndex(
                name: "IX_VehicleCategories_Name",
                table: "VehicleCategories");

            migrationBuilder.DropIndex(
                name: "IX_Transporters_Name",
                table: "Transporters");

            migrationBuilder.DropIndex(
                name: "IX_Regions_Name",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_PONumber",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_Products_SkuCode",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_OutwardTransactions_OutwardTxnNumber",
                table: "OutwardTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Locations_Name",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_InwardTransactions_InwardTxnNumber",
                table: "InwardTransactions");

            migrationBuilder.DropIndex(
                name: "IX_DispatchOrders_DispatchOrderNumber",
                table: "DispatchOrders");

            migrationBuilder.DropIndex(
                name: "IX_Countries_Name",
                table: "Countries");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Warehouses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "VehicleTypes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Vehicles",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "VehicleMasters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "VehicleLogisticsRecords",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "VehicleCategories",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Transporters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "States",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Regions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "PurchaseOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Products",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "OutwardTransactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Locations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "InwardTransactions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "FollowUpTasks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "DockBays",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "DispatchOrders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Countries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "Cities",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "AuditLogs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OrganizationId",
                table: "AspNetUsers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    MaxUsers = table.Column<int>(type: "int", nullable: false),
                    MaxWarehouses = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                });

            // Every row that existed before multi-tenancy belongs to exactly one organization -
            // bootstrap it here (not in SeedData, which only runs after migrations complete and
            // the FK constraints below have already been validated) so existing data has a real
            // Organization to point at instead of the OrganizationId=0/NULL default just added.
            // SeedData.GetOrAddDefaultOrganizationAsync finds this same row by Code afterwards
            // rather than creating a second one.
            migrationBuilder.Sql(@"
                DECLARE @DefaultOrgId INT;
                INSERT INTO [Organizations] ([Name], [Code], [IsActive], [MaxUsers], [MaxWarehouses], [CreatedAtUtc])
                VALUES (N'Godrej Consumer Products Limited', N'GCPL', 1, 0, 0, GETUTCDATE());
                SET @DefaultOrgId = SCOPE_IDENTITY();

                UPDATE [Warehouses] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [VehicleTypes] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [Vehicles] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [VehicleMasters] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [VehicleLogisticsRecords] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [VehicleCategories] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [Transporters] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [States] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [Regions] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [PurchaseOrders] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [Products] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [OutwardTransactions] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [Locations] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [InwardTransactions] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [FollowUpTasks] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [DockBays] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [DispatchOrders] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [Countries] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [Cities] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] = 0;
                UPDATE [AspNetUsers] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] IS NULL;
                UPDATE [AuditLogs] SET [OrganizationId] = @DefaultOrgId WHERE [OrganizationId] IS NULL;
            ");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_OrganizationId",
                table: "Warehouses",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_OrganizationId_Name",
                table: "Warehouses",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTypes_OrganizationId",
                table: "VehicleTypes",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTypes_OrganizationId_Name",
                table: "VehicleTypes",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_OrganizationId",
                table: "Vehicles",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_OrganizationId_Number",
                table: "Vehicles",
                columns: new[] { "OrganizationId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMasters_OrganizationId",
                table: "VehicleMasters",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMasters_OrganizationId_VehicleTypeId_VehicleCategoryId",
                table: "VehicleMasters",
                columns: new[] { "OrganizationId", "VehicleTypeId", "VehicleCategoryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMasters_VehicleTypeId",
                table: "VehicleMasters",
                column: "VehicleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_OrganizationId",
                table: "VehicleLogisticsRecords",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCategories_OrganizationId",
                table: "VehicleCategories",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleCategories_OrganizationId_Name",
                table: "VehicleCategories",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Transporters_OrganizationId",
                table: "Transporters",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Transporters_OrganizationId_Name",
                table: "Transporters",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_States_OrganizationId",
                table: "States",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_OrganizationId",
                table: "Regions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_OrganizationId_Name",
                table: "Regions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrganizationId",
                table: "PurchaseOrders",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_OrganizationId_PONumber",
                table: "PurchaseOrders",
                columns: new[] { "OrganizationId", "PONumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_OrganizationId",
                table: "Products",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_OrganizationId_SkuCode",
                table: "Products",
                columns: new[] { "OrganizationId", "SkuCode" },
                unique: true,
                filter: "[SkuCode] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardTransactions_OrganizationId",
                table: "OutwardTransactions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardTransactions_OrganizationId_OutwardTxnNumber",
                table: "OutwardTransactions",
                columns: new[] { "OrganizationId", "OutwardTxnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_OrganizationId",
                table: "Locations",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_OrganizationId_Name",
                table: "Locations",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InwardTransactions_OrganizationId",
                table: "InwardTransactions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_InwardTransactions_OrganizationId_InwardTxnNumber",
                table: "InwardTransactions",
                columns: new[] { "OrganizationId", "InwardTxnNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_OrganizationId",
                table: "FollowUpTasks",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_DockBays_OrganizationId",
                table: "DockBays",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchOrders_OrganizationId",
                table: "DispatchOrders",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchOrders_OrganizationId_DispatchOrderNumber",
                table: "DispatchOrders",
                columns: new[] { "OrganizationId", "DispatchOrderNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_OrganizationId",
                table: "Countries",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Countries_OrganizationId_Name",
                table: "Countries",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cities_OrganizationId",
                table: "Cities",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OrganizationId",
                table: "AuditLogs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_OrganizationId_NormalizedUserName",
                table: "AspNetUsers",
                columns: new[] { "OrganizationId", "NormalizedUserName" },
                unique: true,
                filter: "[OrganizationId] IS NOT NULL AND [NormalizedUserName] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Organizations_OrganizationId",
                table: "AspNetUsers",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_AuditLogs_Organizations_OrganizationId",
                table: "AuditLogs",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Cities_Organizations_OrganizationId",
                table: "Cities",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Countries_Organizations_OrganizationId",
                table: "Countries",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DispatchOrders_Organizations_OrganizationId",
                table: "DispatchOrders",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_DockBays_Organizations_OrganizationId",
                table: "DockBays",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FollowUpTasks_Organizations_OrganizationId",
                table: "FollowUpTasks",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InwardTransactions_Organizations_OrganizationId",
                table: "InwardTransactions",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Locations_Organizations_OrganizationId",
                table: "Locations",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OutwardTransactions_Organizations_OrganizationId",
                table: "OutwardTransactions",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Organizations_OrganizationId",
                table: "Products",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PurchaseOrders_Organizations_OrganizationId",
                table: "PurchaseOrders",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Regions_Organizations_OrganizationId",
                table: "Regions",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_States_Organizations_OrganizationId",
                table: "States",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Transporters_Organizations_OrganizationId",
                table: "Transporters",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleCategories_Organizations_OrganizationId",
                table: "VehicleCategories",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleLogisticsRecords_Organizations_OrganizationId",
                table: "VehicleLogisticsRecords",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleMasters_Organizations_OrganizationId",
                table: "VehicleMasters",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Vehicles_Organizations_OrganizationId",
                table: "Vehicles",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_VehicleTypes_Organizations_OrganizationId",
                table: "VehicleTypes",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Warehouses_Organizations_OrganizationId",
                table: "Warehouses",
                column: "OrganizationId",
                principalTable: "Organizations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Organizations_OrganizationId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_AuditLogs_Organizations_OrganizationId",
                table: "AuditLogs");

            migrationBuilder.DropForeignKey(
                name: "FK_Cities_Organizations_OrganizationId",
                table: "Cities");

            migrationBuilder.DropForeignKey(
                name: "FK_Countries_Organizations_OrganizationId",
                table: "Countries");

            migrationBuilder.DropForeignKey(
                name: "FK_DispatchOrders_Organizations_OrganizationId",
                table: "DispatchOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_DockBays_Organizations_OrganizationId",
                table: "DockBays");

            migrationBuilder.DropForeignKey(
                name: "FK_FollowUpTasks_Organizations_OrganizationId",
                table: "FollowUpTasks");

            migrationBuilder.DropForeignKey(
                name: "FK_InwardTransactions_Organizations_OrganizationId",
                table: "InwardTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Locations_Organizations_OrganizationId",
                table: "Locations");

            migrationBuilder.DropForeignKey(
                name: "FK_OutwardTransactions_Organizations_OrganizationId",
                table: "OutwardTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_Products_Organizations_OrganizationId",
                table: "Products");

            migrationBuilder.DropForeignKey(
                name: "FK_PurchaseOrders_Organizations_OrganizationId",
                table: "PurchaseOrders");

            migrationBuilder.DropForeignKey(
                name: "FK_Regions_Organizations_OrganizationId",
                table: "Regions");

            migrationBuilder.DropForeignKey(
                name: "FK_States_Organizations_OrganizationId",
                table: "States");

            migrationBuilder.DropForeignKey(
                name: "FK_Transporters_Organizations_OrganizationId",
                table: "Transporters");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleCategories_Organizations_OrganizationId",
                table: "VehicleCategories");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleLogisticsRecords_Organizations_OrganizationId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleMasters_Organizations_OrganizationId",
                table: "VehicleMasters");

            migrationBuilder.DropForeignKey(
                name: "FK_Vehicles_Organizations_OrganizationId",
                table: "Vehicles");

            migrationBuilder.DropForeignKey(
                name: "FK_VehicleTypes_Organizations_OrganizationId",
                table: "VehicleTypes");

            migrationBuilder.DropForeignKey(
                name: "FK_Warehouses_Organizations_OrganizationId",
                table: "Warehouses");

            migrationBuilder.DropTable(
                name: "Organizations");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_OrganizationId",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_Warehouses_OrganizationId_Name",
                table: "Warehouses");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTypes_OrganizationId",
                table: "VehicleTypes");

            migrationBuilder.DropIndex(
                name: "IX_VehicleTypes_OrganizationId_Name",
                table: "VehicleTypes");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_OrganizationId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_OrganizationId_Number",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_VehicleMasters_OrganizationId",
                table: "VehicleMasters");

            migrationBuilder.DropIndex(
                name: "IX_VehicleMasters_OrganizationId_VehicleTypeId_VehicleCategoryId",
                table: "VehicleMasters");

            migrationBuilder.DropIndex(
                name: "IX_VehicleMasters_VehicleTypeId",
                table: "VehicleMasters");

            migrationBuilder.DropIndex(
                name: "IX_VehicleLogisticsRecords_OrganizationId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropIndex(
                name: "IX_VehicleCategories_OrganizationId",
                table: "VehicleCategories");

            migrationBuilder.DropIndex(
                name: "IX_VehicleCategories_OrganizationId_Name",
                table: "VehicleCategories");

            migrationBuilder.DropIndex(
                name: "IX_Transporters_OrganizationId",
                table: "Transporters");

            migrationBuilder.DropIndex(
                name: "IX_Transporters_OrganizationId_Name",
                table: "Transporters");

            migrationBuilder.DropIndex(
                name: "IX_States_OrganizationId",
                table: "States");

            migrationBuilder.DropIndex(
                name: "IX_Regions_OrganizationId",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_Regions_OrganizationId_Name",
                table: "Regions");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_OrganizationId",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_PurchaseOrders_OrganizationId_PONumber",
                table: "PurchaseOrders");

            migrationBuilder.DropIndex(
                name: "IX_Products_OrganizationId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_OrganizationId_SkuCode",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_OutwardTransactions_OrganizationId",
                table: "OutwardTransactions");

            migrationBuilder.DropIndex(
                name: "IX_OutwardTransactions_OrganizationId_OutwardTxnNumber",
                table: "OutwardTransactions");

            migrationBuilder.DropIndex(
                name: "IX_Locations_OrganizationId",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_Locations_OrganizationId_Name",
                table: "Locations");

            migrationBuilder.DropIndex(
                name: "IX_InwardTransactions_OrganizationId",
                table: "InwardTransactions");

            migrationBuilder.DropIndex(
                name: "IX_InwardTransactions_OrganizationId_InwardTxnNumber",
                table: "InwardTransactions");

            migrationBuilder.DropIndex(
                name: "IX_FollowUpTasks_OrganizationId",
                table: "FollowUpTasks");

            migrationBuilder.DropIndex(
                name: "IX_DockBays_OrganizationId",
                table: "DockBays");

            migrationBuilder.DropIndex(
                name: "IX_DispatchOrders_OrganizationId",
                table: "DispatchOrders");

            migrationBuilder.DropIndex(
                name: "IX_DispatchOrders_OrganizationId_DispatchOrderNumber",
                table: "DispatchOrders");

            migrationBuilder.DropIndex(
                name: "IX_Countries_OrganizationId",
                table: "Countries");

            migrationBuilder.DropIndex(
                name: "IX_Countries_OrganizationId_Name",
                table: "Countries");

            migrationBuilder.DropIndex(
                name: "IX_Cities_OrganizationId",
                table: "Cities");

            migrationBuilder.DropIndex(
                name: "IX_AuditLogs_OrganizationId",
                table: "AuditLogs");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_OrganizationId_NormalizedUserName",
                table: "AspNetUsers");

            migrationBuilder.DropIndex(
                name: "UserNameIndex",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Warehouses");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "VehicleTypes");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "VehicleMasters");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "VehicleLogisticsRecords");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "VehicleCategories");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Transporters");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "States");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Regions");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "PurchaseOrders");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "OutwardTransactions");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "InwardTransactions");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "FollowUpTasks");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "DockBays");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "DispatchOrders");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Countries");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "Cities");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "AuditLogs");

            migrationBuilder.DropColumn(
                name: "OrganizationId",
                table: "AspNetUsers");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Name",
                table: "Warehouses",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTypes_Name",
                table: "VehicleTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_Number",
                table: "Vehicles",
                column: "Number",
                unique: true);

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
                name: "IX_Transporters_Name",
                table: "Transporters",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Regions_Name",
                table: "Regions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrders_PONumber",
                table: "PurchaseOrders",
                column: "PONumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Products_SkuCode",
                table: "Products",
                column: "SkuCode",
                unique: true,
                filter: "[SkuCode] <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardTransactions_OutwardTxnNumber",
                table: "OutwardTransactions",
                column: "OutwardTxnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Locations_Name",
                table: "Locations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InwardTransactions_InwardTxnNumber",
                table: "InwardTransactions",
                column: "InwardTxnNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DispatchOrders_DispatchOrderNumber",
                table: "DispatchOrders",
                column: "DispatchOrderNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Countries_Name",
                table: "Countries",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName",
                unique: true,
                filter: "[NormalizedUserName] IS NOT NULL");
        }
    }
}
