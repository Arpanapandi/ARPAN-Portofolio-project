using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddQtyLotOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "QtyLotOverride",
                table: "DeliveryItems",
                type: "INTEGER",
                nullable: true);

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "QtyLotOverride",
                table: "DeliveryItems");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 7, 17, 8, 32, 26, 597, DateTimeKind.Local).AddTicks(6410));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 7, 17, 8, 32, 26, 597, DateTimeKind.Local).AddTicks(6413));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 7, 17, 8, 32, 26, 597, DateTimeKind.Local).AddTicks(6584));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 7, 17, 8, 32, 26, 597, DateTimeKind.Local).AddTicks(6587));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                column: "CreatedDate",
                value: new DateTime(2026, 7, 17, 8, 32, 26, 597, DateTimeKind.Local).AddTicks(6589));
        }
    }
}
