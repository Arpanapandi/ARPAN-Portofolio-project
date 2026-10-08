using dashboardKlinik.Data;
using dashboardKlinik.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace dashboardKlinik.Services
{
    public class GoogleSheetSyncService
    {
        private readonly ApplicationDbContext _context;
        private readonly HttpClient _httpClient;
        private readonly ActivityLogService _activityLog;
        private readonly string _spreadsheetId;

        public GoogleSheetSyncService(
            ApplicationDbContext context,
            HttpClient httpClient,
            ActivityLogService activityLog,
            IConfiguration configuration)
        {
            _context = context;
            _httpClient = httpClient;
            _activityLog = activityLog;
            _spreadsheetId = configuration["GoogleSheet:SpreadsheetId"] ?? "1MmeOuB55rixDaMyVv1QJ-GEvGPxv6mBU-0iByjNyUjE";
        }

        public async Task<(int added, int updated, int errors)> SyncFromGoogleSheetAsync()
        {
            int added = 0, updated = 0, errors = 0;

            try
            {
                var csvUrl = $"https://docs.google.com/spreadsheets/d/{_spreadsheetId}/export?format=csv";
                var csvContent = await _httpClient.GetStringAsync(csvUrl);

                var lines = csvContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);

                if (lines.Length <= 1)
                    return (added, updated, errors);

                // Skip header row
                for (int i = 1; i < lines.Length; i++)
                {
                    try
                    {
                        var columns = ParseCsvLine(lines[i].Trim());
                        if (columns.Length < 12) continue;

                        var sheetId = int.Parse(columns[0].Trim());

                        // Check if kunjungan already exists
                        var existingKunjungan = await _context.KunjunganKlinik
                            .FirstOrDefaultAsync(k => k.Id == sheetId);

                        DateTime timestamp = DateTime.Now;
                        DateTime.TryParseExact(columns[1].Trim(), new[] { "dd/MM/yyyy HH:mm:ss", "M/d/yyyy H:mm:ss", "dd/MM/yyyy H:mm:ss" },
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out timestamp);

                        DateTime tanggalKunjungan = DateTime.Today;
                        DateTime.TryParseExact(columns[2].Trim(), new[] { "dd/MM/yyyy", "M/d/yyyy", "d/MM/yyyy" },
                            CultureInfo.InvariantCulture, DateTimeStyles.None, out tanggalKunjungan);

                        var namaPasien = columns[3].Trim();
                        var npk = columns[4].Trim();
                        var plant = columns[5].Trim();
                        var departemen = columns[6].Trim();
                        var jenisKelamin = columns[7].Trim();
                        var keluhan = columns[8].Trim();
                        var diagnosa = columns[9].Trim();
                        var dokter = columns[10].Trim();
                        var status = columns[11].Trim();

                        // Find or create Pasien
                        var pasien = await _context.Pasien.FirstOrDefaultAsync(p => p.NPK == npk);
                        if (pasien == null)
                        {
                            pasien = new Pasien
                            {
                                NamaPasien = namaPasien,
                                NPK = npk,
                                Plant = plant,
                                Departemen = departemen,
                                JenisKelamin = jenisKelamin,
                                TanggalTerdaftar = tanggalKunjungan
                            };
                            _context.Pasien.Add(pasien);
                            await _context.SaveChangesAsync();
                        }

                        if (existingKunjungan == null)
                        {
                            var kunjungan = new KunjunganKlinik
                            {
                                Timestamp = timestamp,
                                TanggalKunjungan = tanggalKunjungan,
                                NamaPasien = namaPasien,
                                NPK = npk,
                                Plant = plant,
                                Departemen = departemen,
                                JenisKelamin = jenisKelamin,
                                Keluhan = keluhan,
                                Diagnosa = diagnosa,
                                Dokter = dokter,
                                Status = status,
                                PasienId = pasien.Id
                            };

                            _context.KunjunganKlinik.Add(kunjungan);
                            added++;
                        }
                        else
                        {
                            existingKunjungan.Timestamp = timestamp;
                            existingKunjungan.TanggalKunjungan = tanggalKunjungan;
                            existingKunjungan.NamaPasien = namaPasien;
                            existingKunjungan.NPK = npk;
                            existingKunjungan.Plant = plant;
                            existingKunjungan.Departemen = departemen;
                            existingKunjungan.JenisKelamin = jenisKelamin;
                            existingKunjungan.Keluhan = keluhan;
                            existingKunjungan.Diagnosa = diagnosa;
                            existingKunjungan.Dokter = dokter;
                            existingKunjungan.Status = status;
                            existingKunjungan.PasienId = pasien.Id;
                            updated++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        Console.WriteLine($"Error processing row {i}: {ex.Message}");
                    }
                }

                await _context.SaveChangesAsync();
                await _activityLog.LogAsync("SYNC", $"Google Sheet sync completed: {added} added, {updated} updated, {errors} errors", "KunjunganKlinik");
            }
            catch (Exception ex)
            {
                await _activityLog.LogAsync("SYNC_ERROR", $"Google Sheet sync failed: {ex.Message}", "KunjunganKlinik");
                throw;
            }

            return (added, updated, errors);
        }

        private string[] ParseCsvLine(string line)
        {
            var result = new List<string>();
            bool inQuotes = false;
            var current = new System.Text.StringBuilder();

            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (line[i] == ',' && !inQuotes)
                {
                    result.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(line[i]);
                }
            }
            result.Add(current.ToString());

            return result.ToArray();
        }
    }
}
