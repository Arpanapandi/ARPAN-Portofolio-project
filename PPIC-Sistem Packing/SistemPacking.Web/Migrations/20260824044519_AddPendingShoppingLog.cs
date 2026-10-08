using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemPacking.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingShoppingLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingShoppingLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    ScannedBarcode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ScannedQty = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: true),
                    ScannedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingShoppingLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingShoppingLogs_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PendingShoppingLogs_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PendingShoppingLogs_Users_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$I8LD4Bm.EseMSTNqSYiio.OiZI1ntJl/w9oZW.Fs.WMNskTlVcYES");

            migrationBuilder.CreateIndex(
                name: "IX_PendingShoppingLogs_ItemId",
                table: "PendingShoppingLogs",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingShoppingLogs_OperatorId",
                table: "PendingShoppingLogs",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingShoppingLogs_ShiftId",
                table: "PendingShoppingLogs",
                column: "ShiftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingShoppingLogs");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$mdP/N3D4lvP6wEABcZd5OOqfCfrZhpB5ZJ6S6JlPP9phlnSCXaekW");
        }
    }
}
