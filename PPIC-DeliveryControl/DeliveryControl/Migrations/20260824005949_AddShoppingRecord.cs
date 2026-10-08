using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddShoppingRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ShoppingRecords",
                columns: table => new
                {
                    ShoppingId = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: true),
                    Tag = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Kanban = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Plant = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    TargetCustomer = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShoppingRecords", x => x.ShoppingId);
                });

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 24, 7, 59, 47, 784, DateTimeKind.Local).AddTicks(9881));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 24, 7, 59, 47, 784, DateTimeKind.Local).AddTicks(9883));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 24, 7, 59, 47, 785, DateTimeKind.Local).AddTicks(23));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 24, 7, 59, 47, 785, DateTimeKind.Local).AddTicks(27));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 24, 7, 59, 47, 785, DateTimeKind.Local).AddTicks(30));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShoppingRecords");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 21, 15, 35, 54, 809, DateTimeKind.Local).AddTicks(1678));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 21, 15, 35, 54, 809, DateTimeKind.Local).AddTicks(1681));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 21, 15, 35, 54, 809, DateTimeKind.Local).AddTicks(1823));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 21, 15, 35, 54, 809, DateTimeKind.Local).AddTicks(1827));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 8, 21, 15, 35, 54, 809, DateTimeKind.Local).AddTicks(1830));
        }
    }
}
