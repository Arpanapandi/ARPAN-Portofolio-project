using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemPacking.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddItemIdToShoppingLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ItemId",
                table: "ShoppingLogs",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$Y.mxXKwl5AkZp80BrQlfmuFAmeYRrDkS5ei.WwK6cFhCY.rZNc7Nq");

            migrationBuilder.CreateIndex(
                name: "IX_ShoppingLogs_ItemId",
                table: "ShoppingLogs",
                column: "ItemId");

            migrationBuilder.AddForeignKey(
                name: "FK_ShoppingLogs_Items_ItemId",
                table: "ShoppingLogs",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ShoppingLogs_Items_ItemId",
                table: "ShoppingLogs");

            migrationBuilder.DropIndex(
                name: "IX_ShoppingLogs_ItemId",
                table: "ShoppingLogs");

            migrationBuilder.DropColumn(
                name: "ItemId",
                table: "ShoppingLogs");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$Zym58lib1dB3NmzWROpEaOGNIPyOTYN4sCVO9ULT0N3nrhSDm2ikm");
        }
    }
}
