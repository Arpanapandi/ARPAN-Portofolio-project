using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dashboardKlinik.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    EntityType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pasien",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NamaPasien = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NPK = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Plant = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Departemen = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    JenisKelamin = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    TanggalTerdaftar = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pasien", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "KunjunganKlinik",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    TanggalKunjungan = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NamaPasien = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    NPK = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Plant = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Departemen = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    JenisKelamin = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    Keluhan = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Diagnosa = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Dokter = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    PasienId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KunjunganKlinik", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KunjunganKlinik_Pasien_PasienId",
                        column: x => x.PasienId,
                        principalTable: "Pasien",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLog_Timestamp",
                table: "ActivityLog",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_KunjunganKlinik_NPK",
                table: "KunjunganKlinik",
                column: "NPK");

            migrationBuilder.CreateIndex(
                name: "IX_KunjunganKlinik_PasienId",
                table: "KunjunganKlinik",
                column: "PasienId");

            migrationBuilder.CreateIndex(
                name: "IX_KunjunganKlinik_Status",
                table: "KunjunganKlinik",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_KunjunganKlinik_TanggalKunjungan",
                table: "KunjunganKlinik",
                column: "TanggalKunjungan");

            migrationBuilder.CreateIndex(
                name: "IX_Pasien_Departemen",
                table: "Pasien",
                column: "Departemen");

            migrationBuilder.CreateIndex(
                name: "IX_Pasien_NamaPasien",
                table: "Pasien",
                column: "NamaPasien");

            migrationBuilder.CreateIndex(
                name: "IX_Pasien_NPK",
                table: "Pasien",
                column: "NPK",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Pasien_Plant",
                table: "Pasien",
                column: "Plant");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLog");

            migrationBuilder.DropTable(
                name: "KunjunganKlinik");

            migrationBuilder.DropTable(
                name: "Pasien");
        }
    }
}
