namespace DeliveryControl.Models
{
    public class KanbanDashboardViewModel
    {
        public DateTime SelectedDate { get; set; }

        /// <summary>Jumlah grup manifest unik pada tanggal ini.</summary>
        public int TotalJadwal { get; set; }

        /// <summary>Jumlah grup manifest yang sudah Completed.</summary>
        public int SudahDelivery { get; set; }

        /// <summary>Total target kanban (sum semua group).</summary>
        public int TotalKanban { get; set; }

        /// <summary>Total actual kanban ter-scan (sum semua group).</summary>
        public int TotalActKanban { get; set; }

        /// <summary>Jumlah baris VIN yang memiliki Variance > 0 (belum scan penuh).</summary>
        public int KanbanMinus { get; set; }

        public List<KanbanGroupRow> Groups { get; set; } = new();
    }

    public class KanbanGroupRow
    {
        public string Manifest { get; set; } = string.Empty;
        public string Dock { get; set; } = string.Empty;
        public string Cycle { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }

        public int GroupTargetKanban { get; set; }
        public int GroupActKanban { get; set; }
        public int GroupVariance => GroupTargetKanban - GroupActKanban;
        public bool HasMinus => Items.Any(i => i.Variance > 0);

        public List<KanbanItemRow> Items { get; set; } = new();
    }

    public class KanbanItemRow
    {
        public string VIN { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int TargetKanban { get; set; }
        public int ActKanban { get; set; }
        public int Variance => TargetKanban - ActKanban;
    }
}
