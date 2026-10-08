using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeliveryControl.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Tingkat",
                table: "PullingRecords");

            migrationBuilder.DropColumn(
                name: "Tingkat",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Tingkat",
                table: "ItemRackLocations");

            migrationBuilder.AddColumn<string>(
                name: "StatusItem",
                table: "Items",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 7, 13, 11, 43, 0, 618, DateTimeKind.Local).AddTicks(173));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 7, 13, 11, 43, 0, 618, DateTimeKind.Local).AddTicks(176));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                columns: new[] { "CreatedDate", "StatusItem" },
                values: new object[] { new DateTime(2026, 7, 13, 11, 43, 0, 618, DateTimeKind.Local).AddTicks(290), "Reguler" });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                columns: new[] { "CreatedDate", "StatusItem" },
                values: new object[] { new DateTime(2026, 7, 13, 11, 43, 0, 618, DateTimeKind.Local).AddTicks(336), "Reguler" });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                columns: new[] { "CreatedDate", "StatusItem" },
                values: new object[] { new DateTime(2026, 7, 13, 11, 43, 0, 618, DateTimeKind.Local).AddTicks(339), "Reguler" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StatusItem",
                table: "Items");

            migrationBuilder.AddColumn<string>(
                name: "Tingkat",
                table: "PullingRecords",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tingkat",
                table: "Items",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tingkat",
                table: "ItemRackLocations",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 1,
                column: "CreatedDate",
                value: new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5670));

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "CustomerId",
                keyValue: 2,
                column: "CreatedDate",
                value: new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5672));

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 1,
                columns: new[] { "CreatedDate", "Tingkat" },
                values: new object[] { new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5779), null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 2,
                columns: new[] { "CreatedDate", "Tingkat" },
                values: new object[] { new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5781), null });

            migrationBuilder.UpdateData(
                table: "Items",
                keyColumn: "ItemId",
                keyValue: 3,
                columns: new[] { "CreatedDate", "Tingkat" },
                values: new object[] { new DateTime(2026, 6, 12, 10, 40, 10, 989, DateTimeKind.Local).AddTicks(5817), null });
        }
    }
}
