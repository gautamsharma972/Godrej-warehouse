using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WarehouseGate.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialMySqlSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Organizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Code = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MaxUsers = table.Column<int>(type: "int", nullable: false),
                    MaxWarehouses = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Organizations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    RoleId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: true),
                    EntityName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    Action = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedByUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedByName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Summary = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Countries_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DispatchOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    DispatchOrderNumber = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CustomerName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RequestedDate = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchOrders_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WeightKg = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LengthCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    WidthCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    HeightCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    SkuCode = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<int>(type: "int", nullable: false),
                    IsStackable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    MaxStackLayers = table.Column<int>(type: "int", nullable: false),
                    ColorHex = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SkuCodeForUniqueness = table.Column<string>(type: "varchar(255)", nullable: true, computedColumnSql: "(CASE WHEN `SkuCode` <> '' THEN `SkuCode` ELSE NULL END)", stored: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PurchaseOrders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    PONumber = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SupplierName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpectedDeliveryDate = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrders_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Regions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Regions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Regions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Transporters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ContactPhone = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transporters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transporters_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VehicleCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleCategories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleCategories_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Vehicles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MaxWeightKg = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LengthCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    WidthCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vehicles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vehicles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VehicleTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleTypes_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "States",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_States", x => x.Id);
                    table.ForeignKey(
                        name: "FK_States_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_States_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DispatchOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    DispatchOrderId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrderedQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProductId = table.Column<int>(type: "int", nullable: true),
                    DeliveryLocation = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsExtra = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DispatchOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DispatchOrderLines_DispatchOrders_DispatchOrderId",
                        column: x => x.DispatchOrderId,
                        principalTable: "DispatchOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DispatchOrderLines_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PurchaseOrderLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: false),
                    ProductName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExpectedQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    UnitOfMeasure = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PickListQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LoadedQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    IsExtra = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SourceDispatchOrderLineId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PurchaseOrderLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PurchaseOrderLines_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VehicleMasters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    VehicleTypeId = table.Column<int>(type: "int", nullable: false),
                    VehicleCategoryId = table.Column<int>(type: "int", nullable: false),
                    MaxWeightKg = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    LengthCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    WidthCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true),
                    HeightCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleMasters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleMasters_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleMasters_VehicleCategories_VehicleCategoryId",
                        column: x => x.VehicleCategoryId,
                        principalTable: "VehicleCategories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleMasters_VehicleTypes_VehicleTypeId",
                        column: x => x.VehicleTypeId,
                        principalTable: "VehicleTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Cities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StateId = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cities_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Cities_States_StateId",
                        column: x => x.StateId,
                        principalTable: "States",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RegionId = table.Column<int>(type: "int", nullable: false),
                    StateId = table.Column<int>(type: "int", nullable: false),
                    CityId = table.Column<int>(type: "int", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Locations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Locations_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Locations_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Locations_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Locations_States_StateId",
                        column: x => x.StateId,
                        principalTable: "States",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    WarehouseType = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    RegionId = table.Column<int>(type: "int", nullable: false),
                    StateId = table.Column<int>(type: "int", nullable: false),
                    CityId = table.Column<int>(type: "int", nullable: false),
                    CountryId = table.Column<int>(type: "int", nullable: false),
                    SlaTargetMinutes = table.Column<int>(type: "int", nullable: true),
                    DockOperatingHoursPerDay = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true),
                    ShiftHoursPerDay = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Warehouses_Cities_CityId",
                        column: x => x.CityId,
                        principalTable: "Cities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Warehouses_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Warehouses_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Warehouses_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Warehouses_States_StateId",
                        column: x => x.StateId,
                        principalTable: "States",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Role = table.Column<int>(type: "int", nullable: false),
                    DisplayName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OrganizationId = table.Column<int>(type: "int", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    RegionId = table.Column<int>(type: "int", nullable: true),
                    UserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    NormalizedEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EmailConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SecurityStamp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PhoneNumberConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Regions_RegionId",
                        column: x => x.RegionId,
                        principalTable: "Regions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_AspNetUsers_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DockBays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsActive = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    OrganizationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DockBays", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DockBays_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DockBays_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "FollowUpTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityName = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntityId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    Title = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Details = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ResolvedByUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResolvedByName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ResolvedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ResolutionNotes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FollowUpTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FollowUpTasks_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FollowUpTasks_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InwardTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    InwardTxnNumber = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PurchaseOrderId = table.Column<int>(type: "int", nullable: true),
                    SecurityEnteredPoNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GateInTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    GateInBySecurityUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DriverName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DriverMobile = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GateName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GpsLatitude = table.Column<double>(type: "double", nullable: true),
                    GpsLongitude = table.Column<double>(type: "double", nullable: true),
                    IsNewVehicle = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    HasDeliveryDateMismatch = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Remarks = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedSupervisorUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    BayName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DockInTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    UnloadingStartTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DockOutTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GateOutTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GateOutBySecurityUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GatePassToken = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InwardTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InwardTransactions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InwardTransactions_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InwardTransactions_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InwardTransactions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutwardTransactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    DispatchOrderId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    OutwardTxnNumber = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedByOfficeUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedSupervisorUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AssignedTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    VehicleId = table.Column<int>(type: "int", nullable: true),
                    GateInTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GateInBySecurityUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DriverName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DriverMobile = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GateName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GpsLatitude = table.Column<double>(type: "double", nullable: true),
                    GpsLongitude = table.Column<double>(type: "double", nullable: true),
                    GateOutTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    GateOutBySecurityUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GatePassToken = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BayName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DockInTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LoadingStartTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DockOutTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExceptionReason = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExceptionRemarks = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ExceptionReportedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    DispatchReadyConfirmedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ActualLoadingStartedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardTransactions_DispatchOrders_DispatchOrderId",
                        column: x => x.DispatchOrderId,
                        principalTable: "DispatchOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardTransactions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardTransactions_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardTransactions_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ClaimValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderKey = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProviderDisplayName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    RoleId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GoodsReceiptNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    GrnNumber = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GeneratedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    HasExceptions = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptNotes_InwardTransactions_InwardTransactionId",
                        column: x => x.InwardTransactionId,
                        principalTable: "InwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InspectionLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: false),
                    ReceivedQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Condition = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InspectionLines_InwardTransactions_InwardTransactionId",
                        column: x => x.InwardTransactionId,
                        principalTable: "InwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InspectionLines_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "InwardDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FilePath = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UploadedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InwardDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InwardDocuments_InwardTransactions_InwardTransactionId",
                        column: x => x.InwardTransactionId,
                        principalTable: "InwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "PhotoEvidences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FilePath = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CapturedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    PurchaseOrderLineId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhotoEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhotoEvidences_InwardTransactions_InwardTransactionId",
                        column: x => x.InwardTransactionId,
                        principalTable: "InwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PhotoEvidences_PurchaseOrderLines_PurchaseOrderLineId",
                        column: x => x.PurchaseOrderLineId,
                        principalTable: "PurchaseOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UnplannedReceiptLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    InwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    ProductId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutwardDispatchNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OutwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    DispatchNoteNumber = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GeneratedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    IsPartial = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardDispatchNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardDispatchNotes_OutwardTransactions_OutwardTransactionId",
                        column: x => x.OutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutwardGateArrivals",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    VehicleId = table.Column<int>(type: "int", nullable: false),
                    GateInTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    GateInBySecurityUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DriverName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DriverMobile = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GateName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GpsLatitude = table.Column<double>(type: "double", nullable: true),
                    GpsLongitude = table.Column<double>(type: "double", nullable: true),
                    SecurityEnteredDispatchOrderNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LinkedOutwardTransactionId = table.Column<int>(type: "int", nullable: true),
                    LinkedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    LinkedByOfficeUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardGateArrivals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivals_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivals_OutwardTransactions_LinkedOutwardTransac~",
                        column: x => x.LinkedOutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivals_Vehicles_VehicleId",
                        column: x => x.VehicleId,
                        principalTable: "Vehicles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivals_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutwardLoadLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OutwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    DispatchOrderLineId = table.Column<int>(type: "int", nullable: false),
                    LoadedQty = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    LoadSequence = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardLoadLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardLoadLines_DispatchOrderLines_DispatchOrderLineId",
                        column: x => x.DispatchOrderLineId,
                        principalTable: "DispatchOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OutwardLoadLines_OutwardTransactions_OutwardTransactionId",
                        column: x => x.OutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutwardLoadPlanOptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OutwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    Label = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsSelected = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardLoadPlanOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardLoadPlanOptions_OutwardTransactions_OutwardTransactio~",
                        column: x => x.OutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "VehicleLogisticsRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OrganizationId = table.Column<int>(type: "int", nullable: false),
                    VehicleNumber = table.Column<string>(type: "varchar(255)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PoNumber = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    InwardTransactionId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    TransporterName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DriverName = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DriverPhone = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VehicleType = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Sku = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SkuCode = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BoxQuantity = table.Column<int>(type: "int", nullable: false),
                    PickListQuantity = table.Column<int>(type: "int", nullable: true),
                    DepartureDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EtaDateTime = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    FromWarehouseId = table.Column<int>(type: "int", nullable: false),
                    ToWarehouseId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConsumedByInwardTransactionId = table.Column<int>(type: "int", nullable: true),
                    ConsumedByOutwardTransactionId = table.Column<int>(type: "int", nullable: true),
                    InwardClaimStartedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedByUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleLogisticsRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VehicleLogisticsRecords_InwardTransactions_ConsumedByInwardT~",
                        column: x => x.ConsumedByInwardTransactionId,
                        principalTable: "InwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleLogisticsRecords_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleLogisticsRecords_OutwardTransactions_ConsumedByOutwar~",
                        column: x => x.ConsumedByOutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleLogisticsRecords_Warehouses_FromWarehouseId",
                        column: x => x.FromWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VehicleLogisticsRecords_Warehouses_ToWarehouseId",
                        column: x => x.ToWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutwardGateArrivalPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OutwardGateArrivalId = table.Column<int>(type: "int", nullable: false),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FilePath = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CapturedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardGateArrivalPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardGateArrivalPhotos_OutwardGateArrivals_OutwardGateArri~",
                        column: x => x.OutwardGateArrivalId,
                        principalTable: "OutwardGateArrivals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutwardLoadPlanGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OutwardLoadPlanOptionId = table.Column<int>(type: "int", nullable: false),
                    DispatchOrderLineId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ZoneLength = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ZoneWidth = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ZoneHeight = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    PositionXCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PositionYCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PositionZCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DimXCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DimYCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DimZCm = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Orientation = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Rows = table.Column<int>(type: "int", nullable: false),
                    Columns = table.Column<int>(type: "int", nullable: false),
                    Layers = table.Column<int>(type: "int", nullable: false),
                    LoadSequence = table.Column<int>(type: "int", nullable: false),
                    Color = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsLocked = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    CreatedByUserId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmationStatus = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ActualQuantity = table.Column<int>(type: "int", nullable: true),
                    ActualNotes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ConfirmedByUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
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
                        name: "FK_OutwardLoadPlanGroups_OutwardLoadPlanOptions_OutwardLoadPlan~",
                        column: x => x.OutwardLoadPlanOptionId,
                        principalTable: "OutwardLoadPlanOptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "OutwardPhotoEvidences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    OutwardTransactionId = table.Column<int>(type: "int", nullable: false),
                    OutwardLoadPlanGroupId = table.Column<int>(type: "int", nullable: true),
                    DispatchOrderLineId = table.Column<int>(type: "int", nullable: true),
                    Type = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FilePath = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CapturedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutwardPhotoEvidences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutwardPhotoEvidences_DispatchOrderLines_DispatchOrderLineId",
                        column: x => x.DispatchOrderLineId,
                        principalTable: "DispatchOrderLines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutwardPhotoEvidences_OutwardLoadPlanGroups_OutwardLoadPlanG~",
                        column: x => x.OutwardLoadPlanGroupId,
                        principalTable: "OutwardLoadPlanGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OutwardPhotoEvidences_OutwardTransactions_OutwardTransaction~",
                        column: x => x.OutwardTransactionId,
                        principalTable: "OutwardTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_OrganizationId_NormalizedUserName",
                table: "AspNetUsers",
                columns: new[] { "OrganizationId", "NormalizedUserName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_RegionId",
                table: "AspNetUsers",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_WarehouseId",
                table: "AspNetUsers",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_OrganizationId",
                table: "AuditLogs",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_OrganizationId",
                table: "Cities",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Cities_StateId",
                table: "Cities",
                column: "StateId");

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
                name: "IX_DispatchOrderLines_DispatchOrderId",
                table: "DispatchOrderLines",
                column: "DispatchOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_DispatchOrderLines_ProductId",
                table: "DispatchOrderLines",
                column: "ProductId");

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
                name: "IX_DockBays_OrganizationId",
                table: "DockBays",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_DockBays_WarehouseId_Name",
                table: "DockBays",
                columns: new[] { "WarehouseId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_OrganizationId",
                table: "FollowUpTasks",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_FollowUpTasks_WarehouseId",
                table: "FollowUpTasks",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptNotes_InwardTransactionId",
                table: "GoodsReceiptNotes",
                column: "InwardTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InspectionLines_InwardTransactionId",
                table: "InspectionLines",
                column: "InwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_InspectionLines_PurchaseOrderLineId",
                table: "InspectionLines",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_InwardDocuments_InwardTransactionId",
                table: "InwardDocuments",
                column: "InwardTransactionId");

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
                name: "IX_InwardTransactions_PurchaseOrderId",
                table: "InwardTransactions",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_InwardTransactions_VehicleId",
                table: "InwardTransactions",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_InwardTransactions_WarehouseId",
                table: "InwardTransactions",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_CityId",
                table: "Locations",
                column: "CityId");

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
                name: "IX_Locations_RegionId",
                table: "Locations",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Locations_StateId",
                table: "Locations",
                column: "StateId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardDispatchNotes_OutwardTransactionId",
                table: "OutwardDispatchNotes",
                column: "OutwardTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivalPhotos_OutwardGateArrivalId",
                table: "OutwardGateArrivalPhotos",
                column: "OutwardGateArrivalId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivals_LinkedOutwardTransactionId",
                table: "OutwardGateArrivals",
                column: "LinkedOutwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivals_OrganizationId",
                table: "OutwardGateArrivals",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivals_VehicleId",
                table: "OutwardGateArrivals",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardGateArrivals_WarehouseId",
                table: "OutwardGateArrivals",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardLoadLines_DispatchOrderLineId",
                table: "OutwardLoadLines",
                column: "DispatchOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardLoadLines_OutwardTransactionId",
                table: "OutwardLoadLines",
                column: "OutwardTransactionId");

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

            migrationBuilder.CreateIndex(
                name: "IX_OutwardPhotoEvidences_DispatchOrderLineId",
                table: "OutwardPhotoEvidences",
                column: "DispatchOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardPhotoEvidences_OutwardLoadPlanGroupId",
                table: "OutwardPhotoEvidences",
                column: "OutwardLoadPlanGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardPhotoEvidences_OutwardTransactionId",
                table: "OutwardPhotoEvidences",
                column: "OutwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardTransactions_DispatchOrderId",
                table: "OutwardTransactions",
                column: "DispatchOrderId");

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
                name: "IX_OutwardTransactions_VehicleId",
                table: "OutwardTransactions",
                column: "VehicleId");

            migrationBuilder.CreateIndex(
                name: "IX_OutwardTransactions_WarehouseId",
                table: "OutwardTransactions",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoEvidences_InwardTransactionId",
                table: "PhotoEvidences",
                column: "InwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_PhotoEvidences_PurchaseOrderLineId",
                table: "PhotoEvidences",
                column: "PurchaseOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_OrganizationId",
                table: "Products",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_OrganizationId_SkuCodeForUniqueness",
                table: "Products",
                columns: new[] { "OrganizationId", "SkuCodeForUniqueness" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PurchaseOrderLines_PurchaseOrderId",
                table: "PurchaseOrderLines",
                column: "PurchaseOrderId");

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
                name: "IX_Regions_OrganizationId",
                table: "Regions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_Regions_OrganizationId_Name",
                table: "Regions",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_States_CountryId",
                table: "States",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_States_OrganizationId",
                table: "States",
                column: "OrganizationId");

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
                name: "IX_UnplannedReceiptLines_InwardTransactionId",
                table: "UnplannedReceiptLines",
                column: "InwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_UnplannedReceiptLines_ProductId",
                table: "UnplannedReceiptLines",
                column: "ProductId");

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
                name: "IX_VehicleLogisticsRecords_ConsumedByInwardTransactionId",
                table: "VehicleLogisticsRecords",
                column: "ConsumedByInwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_ConsumedByOutwardTransactionId",
                table: "VehicleLogisticsRecords",
                column: "ConsumedByOutwardTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_FromWarehouseId",
                table: "VehicleLogisticsRecords",
                column: "FromWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_OrganizationId",
                table: "VehicleLogisticsRecords",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_Status",
                table: "VehicleLogisticsRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_ToWarehouseId",
                table: "VehicleLogisticsRecords",
                column: "ToWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleLogisticsRecords_VehicleNumber",
                table: "VehicleLogisticsRecords",
                column: "VehicleNumber");

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
                name: "IX_VehicleMasters_VehicleCategoryId",
                table: "VehicleMasters",
                column: "VehicleCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleMasters_VehicleTypeId",
                table: "VehicleMasters",
                column: "VehicleTypeId");

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
                name: "IX_VehicleTypes_OrganizationId",
                table: "VehicleTypes",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_VehicleTypes_OrganizationId_Name",
                table: "VehicleTypes",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_CityId",
                table: "Warehouses",
                column: "CityId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_CountryId",
                table: "Warehouses",
                column: "CountryId");

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
                name: "IX_Warehouses_RegionId",
                table: "Warehouses",
                column: "RegionId");

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_StateId",
                table: "Warehouses",
                column: "StateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "DockBays");

            migrationBuilder.DropTable(
                name: "FollowUpTasks");

            migrationBuilder.DropTable(
                name: "GoodsReceiptNotes");

            migrationBuilder.DropTable(
                name: "InspectionLines");

            migrationBuilder.DropTable(
                name: "InwardDocuments");

            migrationBuilder.DropTable(
                name: "Locations");

            migrationBuilder.DropTable(
                name: "OutwardDispatchNotes");

            migrationBuilder.DropTable(
                name: "OutwardGateArrivalPhotos");

            migrationBuilder.DropTable(
                name: "OutwardLoadLines");

            migrationBuilder.DropTable(
                name: "OutwardPhotoEvidences");

            migrationBuilder.DropTable(
                name: "PhotoEvidences");

            migrationBuilder.DropTable(
                name: "Transporters");

            migrationBuilder.DropTable(
                name: "UnplannedReceiptLines");

            migrationBuilder.DropTable(
                name: "VehicleLogisticsRecords");

            migrationBuilder.DropTable(
                name: "VehicleMasters");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "OutwardGateArrivals");

            migrationBuilder.DropTable(
                name: "OutwardLoadPlanGroups");

            migrationBuilder.DropTable(
                name: "PurchaseOrderLines");

            migrationBuilder.DropTable(
                name: "InwardTransactions");

            migrationBuilder.DropTable(
                name: "VehicleCategories");

            migrationBuilder.DropTable(
                name: "VehicleTypes");

            migrationBuilder.DropTable(
                name: "DispatchOrderLines");

            migrationBuilder.DropTable(
                name: "OutwardLoadPlanOptions");

            migrationBuilder.DropTable(
                name: "PurchaseOrders");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "OutwardTransactions");

            migrationBuilder.DropTable(
                name: "DispatchOrders");

            migrationBuilder.DropTable(
                name: "Vehicles");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.DropTable(
                name: "Cities");

            migrationBuilder.DropTable(
                name: "Regions");

            migrationBuilder.DropTable(
                name: "States");

            migrationBuilder.DropTable(
                name: "Countries");

            migrationBuilder.DropTable(
                name: "Organizations");
        }
    }
}
