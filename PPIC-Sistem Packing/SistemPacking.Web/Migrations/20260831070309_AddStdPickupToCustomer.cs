using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemPacking.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddStdPickupToCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StdPickup",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "Id",
                keyValue: 1,
                column: "StdPickup",
                value: null);

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$03a0C3Vbq6nGBe4eS/aMAesOzUw14oAk.A/aU8T2rBkVcONnapEtG");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StdPickup",
                table: "Customers");

            migrationBuilder.UpdateData(
                table: "Users",
                keyColumn: "Id",
                keyValue: 1,
                column: "PasswordHash",
                value: "$2a$11$Y.mxXKwl5AkZp80BrQlfmuFAmeYRrDkS5ei.WwK6cFhCY.rZNc7Nq");
        }
    }
}
