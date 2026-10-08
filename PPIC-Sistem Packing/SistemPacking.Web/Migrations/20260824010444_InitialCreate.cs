using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SistemPacking.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Areas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AreaCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AreaName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Areas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CustomerCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CustomerName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Address = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ContactPerson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Dock = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Route = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Cycle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    StdShopping = table.Column<int>(type: "int", nullable: true),
                    StdPacking = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManPowers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NPK = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManPowers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReasonRejects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReasonCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReasonRejects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CanManageMaster = table.Column<bool>(type: "bit", nullable: false),
                    CanManageOrder = table.Column<bool>(type: "bit", nullable: false),
                    CanShopping = table.Column<bool>(type: "bit", nullable: false),
                    CanPacking = table.Column<bool>(type: "bit", nullable: false),
                    CanVerify = table.Column<bool>(type: "bit", nullable: false),
                    CanViewReport = table.Column<bool>(type: "bit", nullable: false),
                    CanManageUser = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StartTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    EndTime = table.Column<TimeSpan>(type: "time", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FGLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LocationCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    LocationName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    AreaId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FGLocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FGLocations_Areas_AreaId",
                        column: x => x.AreaId,
                        principalTable: "Areas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    NIK = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Username = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PhoneNumber = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    LastLoginIP = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Users_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Items",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemCode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Barcode = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    UOM = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PackingStandard = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FGLocationId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SpisImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SppsImagePath = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    VIN = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Dock = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Point1 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Point2 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Point3 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Point4 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Point5 = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TypeKarton = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TypePlastik = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ProdPlant = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    LokasiRack = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Rack = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NoRack = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    QtyPcs = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ActQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Min = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Rop = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Max = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Items", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Items_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Items_FGLocations_FGLocationId",
                        column: x => x.FGLocationId,
                        principalTable: "FGLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ActivityLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IPAddress = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UserAgent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Module = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrderUploadHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UploadedById = table.Column<int>(type: "int", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    SuccessRows = table.Column<int>(type: "int", nullable: false),
                    FailedRows = table.Column<int>(type: "int", nullable: false),
                    ErrorLog = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderUploadHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderUploadHistories_Users_UploadedById",
                        column: x => x.UploadedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScanNgLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScannedBarcode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Category = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Module = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: true),
                    ScannedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanNgLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanNgLogs_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScanNgLogs_Users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ItemMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    KanbanCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemMappings_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderNo = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CustomerId = table.Column<int>(type: "int", nullable: false),
                    OrderDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveryNote = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReleasedById = table.Column<int>(type: "int", nullable: true),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledById = table.Column<int>(type: "int", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    UploadHistoryId = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PlanDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PlanETD = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ActualETD = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeliveryRemark = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Orders_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_OrderUploadHistories_UploadHistoryId",
                        column: x => x.UploadHistoryId,
                        principalTable: "OrderUploadHistories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Orders_Users_CancelledById",
                        column: x => x.CancelledById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Orders_Users_ReleasedById",
                        column: x => x.ReleasedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrderDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    TargetQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ActualQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ShoppingQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderDetails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderDetails_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrderDetails_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Verifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    LeaderId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RejectReasonId = table.Column<int>(type: "int", nullable: true),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Verifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Verifications_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Verifications_ReasonRejects_RejectReasonId",
                        column: x => x.RejectReasonId,
                        principalTable: "ReasonRejects",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_Verifications_Users_LeaderId",
                        column: x => x.LeaderId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PackingLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderDetailId = table.Column<int>(type: "int", nullable: false),
                    ScannedBarcode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PackedQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: true),
                    PackedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackingLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackingLogs_OrderDetails_OrderDetailId",
                        column: x => x.OrderDetailId,
                        principalTable: "OrderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackingLogs_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PackingLogs_Users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShoppingLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderDetailId = table.Column<int>(type: "int", nullable: false),
                    ScannedBarcode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScannedQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    InvalidReason = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ScannedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShoppingLogs_OrderDetails_OrderDetailId",
                        column: x => x.OrderDetailId,
                        principalTable: "OrderDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShoppingLogs_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ShoppingLogs_Users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Customers",
                columns: new[] { "Id", "Address", "ContactPerson", "CreatedAt", "CustomerCode", "CustomerName", "Cycle", "Dock", "IsActive", "IsDeleted", "Phone", "Route", "StdPacking", "StdShopping", "UpdatedAt" },
                values: new object[] { 1, null, null, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "CUST001", "Default Customer", null, null, true, false, null, null, null, null, null });

            migrationBuilder.InsertData(
                table: "ReasonRejects",
                columns: new[] { "Id", "CreatedAt", "Description", "IsActive", "IsDeleted", "ReasonCode", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Qty tidak sesuai", true, false, "R001", null },
                    { 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Item tidak sesuai", true, false, "R002", null },
                    { 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Packing rusak", true, false, "R003", null },
                    { 4, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Lainnya", true, false, "R004", null }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "CanManageMaster", "CanManageOrder", "CanManageUser", "CanPacking", "CanShopping", "CanVerify", "CanViewReport", "CreatedAt", "Description", "IsDeleted", "RoleName", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, true, true, true, true, true, true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Full access", false, "Super Admin", null },
                    { 2, false, true, false, false, false, true, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Leader access", false, "Leader", null },
                    { 3, false, false, false, false, true, false, false, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Shopping operator", false, "Operator Shopping", null },
                    { 4, false, false, false, true, false, false, false, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Packing operator", false, "Operator Packing", null },
                    { 5, false, false, false, false, false, false, true, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), "Read-only access", false, "Viewer", null }
                });

            migrationBuilder.InsertData(
                table: "Shifts",
                columns: new[] { "Id", "CreatedAt", "EndTime", "IsActive", "IsDeleted", "ShiftName", "StartTime", "UpdatedAt" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 15, 0, 0, 0), true, false, "Shift 1 (Pagi)", new TimeSpan(0, 7, 0, 0, 0), null },
                    { 2, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 23, 0, 0, 0), true, false, "Shift 2 (Siang)", new TimeSpan(0, 15, 0, 0, 0), null },
                    { 3, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 7, 0, 0, 0), true, false, "Shift 3 (Malam)", new TimeSpan(0, 23, 0, 0, 0), null }
                });

            migrationBuilder.InsertData(
                table: "Items",
                columns: new[] { "Id", "ActQty", "Barcode", "Category", "CreatedAt", "CustomerId", "Description", "Dock", "FGLocationId", "IsDeleted", "ItemCode", "ItemName", "LokasiRack", "Max", "Min", "NoRack", "PackingStandard", "Point1", "Point2", "Point3", "Point4", "Point5", "ProdPlant", "QtyPcs", "Rack", "Rop", "SpisImagePath", "SppsImagePath", "Status", "TypeKarton", "TypePlastik", "UOM", "UpdatedAt", "VIN" },
                values: new object[,]
                {
                    { 1, 0m, "LBL-TA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "TA1234", "Item TA", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 2, 0m, "LBL-NA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "NA1234", "Item NA", null, 0m, 0m, null, 20m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 3, 0m, "LBL-HN1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "HN1234", "Item HN", null, 0m, 0m, null, 50m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 4, 0m, "LBL-ZA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "ZA1234", "Item ZA", null, 0m, 0m, null, 15m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 5, 0m, "LBL-YA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "YA1234", "Item YA", null, 0m, 0m, null, 25m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 6, 0m, "LBL-XA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "XA1234", "Item XA", null, 0m, 0m, null, 30m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 7, 0m, "LBL-WA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "WA1234", "Item WA", null, 0m, 0m, null, 12m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 8, 0m, "LBL-VA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "VA1234", "Item VA", null, 0m, 0m, null, 8m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 9, 0m, "LBL-UA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "UA1234", "Item UA", null, 0m, 0m, null, 40m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 10, 0m, "LBL-SA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "SA1234", "Item SA", null, 0m, 0m, null, 60m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 11, 0m, "LBL-RA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "RA1234", "Item RA", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 12, 0m, "LBL-QA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "QA1234", "Item QA", null, 0m, 0m, null, 5m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 13, 0m, "LBL-PA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "PA1234", "Item PA", null, 0m, 0m, null, 100m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 14, 0m, "LBL-OA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "OA1234", "Item OA", null, 0m, 0m, null, 50m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 15, 0m, "LBL-MA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "MA1234", "Item MA", null, 0m, 0m, null, 20m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 16, 0m, "LBL-LA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "LA1234", "Item LA", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 17, 0m, "LBL-KA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "KA1234", "Item KA", null, 0m, 0m, null, 15m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 18, 0m, "LBL-JA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "JA1234", "Item JA", null, 0m, 0m, null, 30m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 19, 0m, "LBL-IA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "IA1234", "Item IA", null, 0m, 0m, null, 25m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 20, 0m, "LBL-GA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "GA1234", "Item GA", null, 0m, 0m, null, 18m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 21, 0m, "LBL-FA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "FA1234", "Item FA", null, 0m, 0m, null, 22m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 22, 0m, "LBL-EA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "EA1234", "Item EA", null, 0m, 0m, null, 12m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 23, 0m, "LBL-DA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DA1234", "Item DA", null, 0m, 0m, null, 14m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 24, 0m, "LBL-CA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "CA1234", "Item CA", null, 0m, 0m, null, 16m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 25, 0m, "LBL-BA1234", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "BA1234", "Item BA", null, 0m, 0m, null, 24m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 26, 0m, "LBL-DUMMY0026", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0026", "Item DUMMY0026", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 27, 0m, "LBL-DUMMY0027", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0027", "Item DUMMY0027", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 28, 0m, "LBL-DUMMY0028", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0028", "Item DUMMY0028", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 29, 0m, "LBL-DUMMY0029", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0029", "Item DUMMY0029", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 30, 0m, "LBL-DUMMY0030", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0030", "Item DUMMY0030", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 31, 0m, "LBL-DUMMY0031", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0031", "Item DUMMY0031", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 32, 0m, "LBL-DUMMY0032", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0032", "Item DUMMY0032", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 33, 0m, "LBL-DUMMY0033", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0033", "Item DUMMY0033", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 34, 0m, "LBL-DUMMY0034", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0034", "Item DUMMY0034", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 35, 0m, "LBL-DUMMY0035", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0035", "Item DUMMY0035", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 36, 0m, "LBL-DUMMY0036", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0036", "Item DUMMY0036", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 37, 0m, "LBL-DUMMY0037", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0037", "Item DUMMY0037", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 38, 0m, "LBL-DUMMY0038", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0038", "Item DUMMY0038", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 39, 0m, "LBL-DUMMY0039", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0039", "Item DUMMY0039", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 40, 0m, "LBL-DUMMY0040", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0040", "Item DUMMY0040", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 41, 0m, "LBL-DUMMY0041", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0041", "Item DUMMY0041", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 42, 0m, "LBL-DUMMY0042", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0042", "Item DUMMY0042", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 43, 0m, "LBL-DUMMY0043", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0043", "Item DUMMY0043", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 44, 0m, "LBL-DUMMY0044", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0044", "Item DUMMY0044", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 45, 0m, "LBL-DUMMY0045", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0045", "Item DUMMY0045", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 46, 0m, "LBL-DUMMY0046", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0046", "Item DUMMY0046", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 47, 0m, "LBL-DUMMY0047", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0047", "Item DUMMY0047", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 48, 0m, "LBL-DUMMY0048", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0048", "Item DUMMY0048", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 49, 0m, "LBL-DUMMY0049", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0049", "Item DUMMY0049", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 50, 0m, "LBL-DUMMY0050", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0050", "Item DUMMY0050", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 51, 0m, "LBL-DUMMY0051", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0051", "Item DUMMY0051", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 52, 0m, "LBL-DUMMY0052", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0052", "Item DUMMY0052", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 53, 0m, "LBL-DUMMY0053", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0053", "Item DUMMY0053", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 54, 0m, "LBL-DUMMY0054", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0054", "Item DUMMY0054", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 55, 0m, "LBL-DUMMY0055", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0055", "Item DUMMY0055", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 56, 0m, "LBL-DUMMY0056", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0056", "Item DUMMY0056", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 57, 0m, "LBL-DUMMY0057", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0057", "Item DUMMY0057", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 58, 0m, "LBL-DUMMY0058", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0058", "Item DUMMY0058", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 59, 0m, "LBL-DUMMY0059", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0059", "Item DUMMY0059", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null },
                    { 60, 0m, "LBL-DUMMY0060", "Export", new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), 1, null, null, null, false, "DUMMY0060", "Item DUMMY0060", null, 0m, 0m, null, 10m, null, null, null, null, null, null, 0m, null, 0m, null, null, 1, null, null, "PCS", null, null }
                });

            migrationBuilder.InsertData(
                table: "Users",
                columns: new[] { "Id", "CreatedAt", "Email", "FullName", "IsActive", "IsDeleted", "LastLoginAt", "LastLoginIP", "NIK", "PasswordHash", "PhoneNumber", "RoleId", "UpdatedAt", "Username" },
                values: new object[] { 1, new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), null, "Super Administrator", true, false, null, null, "ADM001", "$2a$11$mdP/N3D4lvP6wEABcZd5OOqfCfrZhpB5ZJ6S6JlPP9phlnSCXaekW", null, 1, null, "admin" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogs_UserId",
                table: "ActivityLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Areas_AreaCode",
                table: "Areas",
                column: "AreaCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerCode",
                table: "Customers",
                column: "CustomerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FGLocations_AreaId",
                table: "FGLocations",
                column: "AreaId");

            migrationBuilder.CreateIndex(
                name: "IX_FGLocations_LocationCode",
                table: "FGLocations",
                column: "LocationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ItemMappings_ItemId",
                table: "ItemMappings",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Barcode",
                table: "Items",
                column: "Barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Items_CustomerId",
                table: "Items",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_FGLocationId",
                table: "Items",
                column: "FGLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_Items_ItemCode",
                table: "Items",
                column: "ItemCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_ItemId",
                table: "OrderDetails",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderDetails_OrderId",
                table: "OrderDetails",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CancelledById",
                table: "Orders",
                column: "CancelledById");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_CustomerId",
                table: "Orders",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderNo",
                table: "Orders",
                column: "OrderNo",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ReleasedById",
                table: "Orders",
                column: "ReleasedById");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_UploadHistoryId",
                table: "Orders",
                column: "UploadHistoryId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderUploadHistories_UploadedById",
                table: "OrderUploadHistories",
                column: "UploadedById");

            migrationBuilder.CreateIndex(
                name: "IX_PackingLogs_OperatorId",
                table: "PackingLogs",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingLogs_OrderDetailId",
                table: "PackingLogs",
                column: "OrderDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_PackingLogs_ShiftId",
                table: "PackingLogs",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanNgLogs_OperatorId",
                table: "ScanNgLogs",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_ScanNgLogs_ShiftId",
                table: "ScanNgLogs",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLogs_OperatorId",
                table: "ShoppingLogs",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLogs_OrderDetailId",
                table: "ShoppingLogs",
                column: "OrderDetailId");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLogs_ShiftId",
                table: "ShoppingLogs",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_NIK",
                table: "Users",
                column: "NIK",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_RoleId",
                table: "Users",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Verifications_LeaderId",
                table: "Verifications",
                column: "LeaderId");

            migrationBuilder.CreateIndex(
                name: "IX_Verifications_OrderId",
                table: "Verifications",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Verifications_RejectReasonId",
                table: "Verifications",
                column: "RejectReasonId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogs");

            migrationBuilder.DropTable(
                name: "ItemMappings");

            migrationBuilder.DropTable(
                name: "ManPowers");

            migrationBuilder.DropTable(
                name: "PackingLogs");

            migrationBuilder.DropTable(
                name: "ScanNgLogs");

            migrationBuilder.DropTable(
                name: "ShoppingLogs");

            migrationBuilder.DropTable(
                name: "Verifications");

            migrationBuilder.DropTable(
                name: "OrderDetails");

            migrationBuilder.DropTable(
                name: "Shifts");

            migrationBuilder.DropTable(
                name: "ReasonRejects");

            migrationBuilder.DropTable(
                name: "Items");

            migrationBuilder.DropTable(
                name: "Orders");

            migrationBuilder.DropTable(
                name: "FGLocations");

            migrationBuilder.DropTable(
                name: "Customers");

            migrationBuilder.DropTable(
                name: "OrderUploadHistories");

            migrationBuilder.DropTable(
                name: "Areas");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Roles");
        }
    }
}
