using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace dashboardKlinik.Migrations
{
    /// <inheritdoc />
    public partial class AddAntrianKlinikTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AntrianKlinik",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NomorAntrian = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    NomorUrut = table.Column<int>(type: "INTEGER", nullable: false),
                    Tanggal = table.Column<DateTime>(type: "TEXT", nullable: false),
                    WaktuDaftar = table.Column<DateTime>(type: "TEXT", nullable: false),
                    NPK = table.Column<string>(type: "TEXT", maxLength: 4, nullable: false),
                    NamaPasien = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Plant = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Departemen = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    JenisKelamin = table.Column<string>(type: "TEXT", maxLength: 1, nullable: false),
                    Keluhan = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    StatusAntrian = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    WaktuDipanggil = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WaktuSelesai = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PasienId = table.Column<int>(type: "INTEGER", nullable: true),
                    KunjunganId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AntrianKlinik", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AntrianKlinik_KunjunganKlinik_KunjunganId",
                        column: x => x.KunjunganId,
                        principalTable: "KunjunganKlinik",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AntrianKlinik_Pasien_PasienId",
                        column: x => x.PasienId,
                        principalTable: "Pasien",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AntrianKlinik_KunjunganId",
                table: "AntrianKlinik",
                column: "KunjunganId");

            migrationBuilder.CreateIndex(
                name: "IX_AntrianKlinik_NPK",
                table: "AntrianKlinik",
                column: "NPK");

            migrationBuilder.CreateIndex(
                name: "IX_AntrianKlinik_PasienId",
                table: "AntrianKlinik",
                column: "PasienId");

            migrationBuilder.CreateIndex(
                name: "IX_AntrianKlinik_StatusAntrian",
                table: "AntrianKlinik",
                column: "StatusAntrian");

            migrationBuilder.CreateIndex(
                name: "IX_AntrianKlinik_Tanggal",
                table: "AntrianKlinik",
                column: "Tanggal");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AntrianKlinik");
        }
    }
}
