using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class RenameRackSuffixToTingkat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    LogId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Module = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    EntityId = table.Column<int>(type: "int", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OldData = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    NewData = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PerformedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IpAddress = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.LogId);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    CustomerId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AutoCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    CustomerCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Route = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cycle = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Docking = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Pickup = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ETD = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Range = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SKID = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    RemarkOrder = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    StdPrepareTime = table.Column<int>(type: "int", nullable: false),
                    StartPrepareTime = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.CustomerId);
                });

            migrationBuilder.CreateTable(
                name: "ItemMappings",
                columns: table => new
                {
                    MappingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VIN = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Customer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerPartNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemMappings", x => x.MappingId);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    ItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Weight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Volume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    MinStock = table.Column<int>(type: "int", nullable: false),
                    MaxStock = table.Column<int>(type: "int", nullable: false),
                    Plant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Rack = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    NoRack = table.Column<int>(type: "int", nullable: true),
                    Tingkat = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    RackAlt = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: true),
                    NoRackAlt = table.Column<int>(type: "int", nullable: true),
                    RackAltCapacity = table.Column<int>(type: "int", nullable: true),
                    Customer = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    VIN = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    QtyLot = table.Column<int>(type: "int", nullable: true),
                    RackMin = table.Column<int>(type: "int", nullable: true),
                    ROP = table.Column<int>(type: "int", nullable: true),
                    RackMax = table.Column<int>(type: "int", nullable: true),
                    CustomerPartNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    KanbanType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false),
                    Remark = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.ItemId);
                });

            migrationBuilder.CreateTable(
                name: "ScanNGLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Module = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Tag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kanban = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanNGLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SmartImportHeaderMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TargetField = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    HeaderKeywords = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmartImportHeaderMaps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SnapshotDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SnapshotTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Plant = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StockQty = table.Column<int>(type: "int", nullable: false),
                    DaysCoverage = table.Column<decimal>(type: "decimal(10,4)", nullable: false),
                    StockLevel = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsManual = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SystemSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SystemSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Password = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    HasAllDockAccess = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Docks",
                columns: table => new
                {
                    DockId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    DockCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DockName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Location = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Docks", x => x.DockId);
                    table.ForeignKey(
                        name: "FK_Docks_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemRackLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Rack = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    NoRack = table.Column<int>(type: "int", nullable: false),
                    Tingkat = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Plant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemRackLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemRackLocations_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PullingRecords",
                columns: table => new
                {
                    PullingId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Plant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rack = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Column = table.Column<int>(type: "int", nullable: false),
                    Tingkat = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Tag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ItemId = table.Column<int>(type: "int", nullable: true),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    IsManualAdjust = table.Column<bool>(type: "bit", nullable: false),
                    AdjustNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PullingRecords", x => x.PullingId);
                    table.ForeignKey(
                        name: "FK_PullingRecords_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleTemplateMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    TargetField = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SourceKeywords = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsHeader = table.Column<bool>(type: "bit", nullable: false),
                    ColumnIndex = table.Column<int>(type: "int", nullable: true),
                    RegexPattern = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleTemplateMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleTemplateMappings_ScheduleTemplates_TemplateId",
                        column: x => x.TemplateId,
                        principalTable: "ScheduleTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserDocks",
                columns: table => new
                {
                    UserDockId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    AssignedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDocks", x => x.UserDockId);
                    table.ForeignKey(
                        name: "FK_UserDocks_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDocks_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeliverySchedules",
                columns: table => new
                {
                    ScheduleId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    CustomerId = table.Column<int>(type: "int", nullable: true),
                    Route = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Cycle = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    EnterDockTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PickupTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ETD = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Range = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SKID = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Area = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    TotalTargetQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TotalActualQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ActualPickupTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ScheduledDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActualEnterDockTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualStartTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualEndTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReadyToDockTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActPrepareTime = table.Column<double>(type: "float", nullable: true),
                    StartPrepareTime = table.Column<int>(type: "int", nullable: false),
                    StdPrepareTime = table.Column<int>(type: "int", nullable: false),
                    PreparationStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DriverStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VehicleNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DriverName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    DriverPhone = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsLeaderVerified = table.Column<bool>(type: "bit", nullable: false),
                    LeaderVerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LeaderVerifiedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LeaderVerifiedKanbanCount = table.Column<int>(type: "int", nullable: false),
                    DockId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliverySchedules", x => x.ScheduleId);
                    table.ForeignKey(
                        name: "FK_DeliverySchedules_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DeliverySchedules_Docks_DockId",
                        column: x => x.DockId,
                        principalTable: "Docks",
                        principalColumn: "DockId");
                });

            migrationBuilder.CreateTable(
                name: "UserDockAccesses",
                columns: table => new
                {
                    UserDockAccessId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    DockId = table.Column<int>(type: "int", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserDockAccesses", x => x.UserDockAccessId);
                    table.ForeignKey(
                        name: "FK_UserDockAccesses_Docks_DockId",
                        column: x => x.DockId,
                        principalTable: "Docks",
                        principalColumn: "DockId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserDockAccesses_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DeliveryItems",
                columns: table => new
                {
                    DeliveryItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ActualQuantity = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Unit = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    TotalWeight = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    TotalVolume = table.Column<decimal>(type: "decimal(18,2)", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ExternalPartNo = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PrepScanTime = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeliveryItems", x => x.DeliveryItemId);
                    table.ForeignKey(
                        name: "FK_DeliveryItems_DeliverySchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "DeliverySchedules",
                        principalColumn: "ScheduleId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DeliveryItems_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "ItemId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PreparationRecords",
                columns: table => new
                {
                    PreparationId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Plant = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Rack = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: true),
                    Column = table.Column<int>(type: "int", nullable: true),
                    Tag = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kanban = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ScheduleId = table.Column<int>(type: "int", nullable: true),
                    ManifestNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Remark = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    DockName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    DockCycle = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DockCustomerId = table.Column<int>(type: "int", nullable: true),
                    TargetDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PreparationRecords", x => x.PreparationId);
                    table.ForeignKey(
                        name: "FK_PreparationRecords_DeliverySchedules_ScheduleId",
                        column: x => x.ScheduleId,
                        principalTable: "DeliverySchedules",
                        principalColumn: "ScheduleId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Customers",
                columns: new[] { "CustomerId", "Area", "AutoCode", "CreatedDate", "CustomerCode", "CustomerName", "Cycle", "Docking", "ETD", "IsActive", "Pickup", "Range", "RemarkOrder", "Route", "SKID", "StartPrepareTime", "StdPrepareTime", "UpdatedDate" },
                values: new object[,]
                {
                    { 1, null, null, new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5670), "CUST001", "PT ABC Manufacturing", null, null, null, true, null, null, null, "Route A", "10 SKID", 0, 0, null },
                    { 2, null, null, new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5672), "CUST002", "PT XYZ Industries", null, null, null, true, null, null, null, "Route B", "15 SKID", 0, 0, null }
                });

            migrationBuilder.InsertData(
                table: "Items",
                columns: new[] { "ItemId", "Category", "CreatedDate", "Customer", "CustomerPartNumber", "Description", "IsActive", "IsDeleted", "ItemCode", "ItemName", "KanbanType", "MaxStock", "MinStock", "NoRack", "NoRackAlt", "Plant", "QtyLot", "ROP", "Rack", "RackAlt", "RackAltCapacity", "RackMax", "RackMin", "Remark", "Tingkat", "Unit", "UpdatedDate", "VIN", "Volume", "Weight" },
                values: new object[,]
                {
                    { 1, "Raw Material", new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5779), null, null, "Raw material untuk produksi", true, false, "ITM001", "Raw Material A", null, 20, 5, null, null, null, null, null, null, null, null, null, null, null, null, "KG", null, null, null, 1.0m },
                    { 2, "Finished Goods", new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5781), null, null, "Produk jadi siap kirim", true, false, "ITM002", "Finished Product B", null, 20, 5, null, null, null, null, null, null, null, null, null, null, null, null, "PCS", null, null, null, 2.5m },
                    { 3, "Packaging", new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5817), null, null, "Material packaging", true, false, "ITM003", "Packaging Material", null, 20, 5, null, null, null, null, null, null, null, null, null, null, null, null, "BOX", null, null, null, 0.5m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_Module",
                table: "ActivityLogs",
                column: "Module");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_Module_Action",
                table: "ActivityLogs",
                columns: new[] { "Module", "Action" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_Timestamp",
                table: "ActivityLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerCode_CustomerName_Route_Cycle_Docking_Pickup_ETD_Range_SKID_Area",
                table: "Customers",
                columns: new[] { "CustomerCode", "CustomerName", "Route", "Cycle", "Docking", "Pickup", "ETD", "Range", "SKID", "Area" },
                unique: true,
                filter: "[Route] IS NOT NULL AND [Cycle] IS NOT NULL AND [Docking] IS NOT NULL AND [Pickup] IS NOT NULL AND [ETD] IS NOT NULL AND [Range] IS NOT NULL AND [SKID] IS NOT NULL AND [Area] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryItems_ItemId",
                table: "DeliveryItems",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliveryItems_ScheduleId_ItemId",
                table: "DeliveryItems",
                columns: new[] { "ScheduleId", "ItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_CreatedDate",
                table: "DeliverySchedules",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_CustomerId_ScheduledDate",
                table: "DeliverySchedules",
                columns: new[] { "CustomerId", "ScheduledDate" });

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_DockId",
                table: "DeliverySchedules",
                column: "DockId");

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_ScheduledDate",
                table: "DeliverySchedules",
                column: "ScheduledDate");

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_ScheduleNumber",
                table: "DeliverySchedules",
                column: "ScheduleNumber",
                unique: true,
                filter: "[ScheduleNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DeliverySchedules_Status",
                table: "DeliverySchedules",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Docks_CustomerId",
                table: "Docks",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemRackLocations_ItemId_Rack_NoRack",
                table: "ItemRackLocations",
                columns: new[] { "ItemId", "Rack", "NoRack" });

            migrationBuilder.CreateIndex(
                name: "IX_Items_CreatedDate",
                table: "Items",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ItemCode",
                table: "Items",
                column: "ItemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_VIN",
                table: "Items",
                column: "VIN");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_CreatedDate",
                table: "PreparationRecords",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_Label",
                table: "PreparationRecords",
                column: "Label");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_ScheduleId",
                table: "PreparationRecords",
                column: "ScheduleId");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_Tag",
                table: "PreparationRecords",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_PreparationRecords_Tag_Label",
                table: "PreparationRecords",
                columns: new[] { "Tag", "Label" });

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_CreatedDate",
                table: "PullingRecords",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_ItemId",
                table: "PullingRecords",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_Label",
                table: "PullingRecords",
                column: "Label");

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_Tag",
                table: "PullingRecords",
                column: "Tag");

            migrationBuilder.CreateIndex(
                name: "IX_PullingRecords_Tag_Label",
                table: "PullingRecords",
                columns: new[] { "Tag", "Label" });

            migrationBuilder.CreateIndex(
                name: "IX_ScanNGLogs_CreatedBy",
                table: "ScanNGLogs",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_ScanNGLogs_CreatedDate",
                table: "ScanNGLogs",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ScanNGLogs_Module",
                table: "ScanNGLogs",
                column: "Module");

            migrationBuilder.CreateIndex(
                name: "IX_ScanNGLogs_Module_CreatedDate",
                table: "ScanNGLogs",
                columns: new[] { "Module", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTemplateMappings_TemplateId",
                table: "ScheduleTemplateMappings",
                column: "TemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTemplates_CustomerName",
                table: "ScheduleTemplates",
                column: "CustomerName");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleTemplates_Name",
                table: "ScheduleTemplates",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_ItemCode_SnapshotDate",
                table: "StockSnapshots",
                columns: new[] { "ItemCode", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_Plant_SnapshotDate",
                table: "StockSnapshots",
                columns: new[] { "Plant", "SnapshotDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockSnapshots_SnapshotDate",
                table: "StockSnapshots",
                column: "SnapshotDate");

            migrationBuilder.CreateIndex(
                name: "IX_SystemSettings_Key",
                table: "SystemSettings",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDockAccesses_DockId",
                table: "UserDockAccesses",
                column: "DockId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDockAccesses_UserId_DockId",
                table: "UserDockAccesses",
                columns: new[] { "UserId", "DockId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserDocks_CustomerId",
                table: "UserDocks",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_UserDocks_UserId",
                table: "UserDocks",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogs");

            migrationBuilder.DropTable(
                name: "DeliveryItems");

            migrationBuilder.DropTable(
                name: "ItemMappings");

            migrationBuilder.DropTable(
                name: "ItemRackLocations");

            migrationBuilder.DropTable(
                name: "PreparationRecords");

            migrationBuilder.DropTable(
                name: "PullingRecords");

            migrationBuilder.DropTable(
                name: "ScanNGLogs");

            migrationBuilder.DropTable(
                name: "ScheduleTemplateMappings");

            migrationBuilder.DropTable(
                name: "SmartImportHeaderMaps");

            migrationBuilder.DropTable(
                name: "StockSnapshots");

            migrationBuilder.DropTable(
                name: "SystemSettings");

            migrationBuilder.DropTable(
                name: "UserDockAccesses");

            migrationBuilder.DropTable(
                name: "UserDocks");

            migrationBuilder.DropTable(
                name: "DeliverySchedules");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "ScheduleTemplates");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Docks");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
