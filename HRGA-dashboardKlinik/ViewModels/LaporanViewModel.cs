namespace dashboardKlinik.ViewModels
{
    public class LaporanViewModel
    {
        public int Bulan { get; set; }
        public int Tahun { get; set; }
        public string NamaBulan { get; set; } = string.Empty;

        public int TotalKunjungan { get; set; }
        public int TotalPasienUnik { get; set; }
        public int RawatJalan { get; set; }
        public int Perawatan { get; set; }
        public int Dirujuk { get; set; }

        public List<ChartDataItem> KunjunganPerDepartemen { get; set; } = new();
        public List<ChartDataItem> DiagnosaPopuler { get; set; } = new();
        public List<ChartDataItem> KunjunganPerHari { get; set; } = new();

        public List<Models.KunjunganKlinik> DataKunjungan { get; set; } = new();
    }
}
