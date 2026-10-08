namespace dashboardKlinik.ViewModels
{
    public class DashboardViewModel
    {
        public int TotalPasien { get; set; }
        public int TotalKunjungan { get; set; }
        public int KunjunganHariIni { get; set; }
        public int KunjunganBulanIni { get; set; }

        // Statistik Status
        public int RawatJalan { get; set; }
        public int Perawatan { get; set; }
        public int Dirujuk { get; set; }

        // Statistik Gender
        public int PasienLakiLaki { get; set; }
        public int PasienPerempuan { get; set; }

        // Data untuk Grafik
        public List<ChartDataItem> KunjunganPerBulan { get; set; } = new();
        public List<ChartDataItem> KunjunganPerDepartemen { get; set; } = new();
        public List<ChartDataItem> KunjunganPerPlant { get; set; } = new();
        public List<ChartDataItem> DiagnosaPopuler { get; set; } = new();
        public List<ChartDataItem> KunjunganPerHari { get; set; } = new();
        public List<ChartDataItem> StatusDistribusi { get; set; } = new();

        // Kunjungan Terbaru
        public List<KunjunganRecentItem> KunjunganTerbaru { get; set; } = new();

        // Aktivitas Terbaru
        public List<ActivityRecentItem> AktivitasTerbaru { get; set; } = new();
    }

    public class ChartDataItem
    {
        public string Label { get; set; } = string.Empty;
        public int Value { get; set; }
        public string Color { get; set; } = string.Empty;
    }

    public class KunjunganRecentItem
    {
        public int Id { get; set; }
        public string NamaPasien { get; set; } = string.Empty;
        public string Plant { get; set; } = string.Empty;
        public string Departemen { get; set; } = string.Empty;
        public string Keluhan { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Dokter { get; set; } = string.Empty;
        public DateTime TanggalKunjungan { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ActivityRecentItem
    {
        public string Action { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
