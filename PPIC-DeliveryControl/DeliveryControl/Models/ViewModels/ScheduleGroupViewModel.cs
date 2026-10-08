using DeliveryControl.Models;

namespace DeliveryControl.Models.ViewModels
{
    /// <summary>
    /// ViewModel untuk menampilkan kelompok jadwal di halaman Jadwal Delivery.
    /// Dikelompokkan berdasarkan: ManifestBase + Dock + Route + Cycle.
    /// </summary>
    public class ScheduleGroupViewModel
    {
        /// <summary>Nomor manifest (bagian sebelum '/' dari ScheduleNumber).</summary>
        public string ManifestBase { get; set; } = string.Empty;

        /// <summary>Dock/Area tujuan.</summary>
        public string Dock { get; set; } = string.Empty;

        /// <summary>Route pengiriman.</summary>
        public string Route { get; set; } = string.Empty;

        /// <summary>Cycle pengiriman (C1, C2, dll).</summary>
        public string Cycle { get; set; } = string.Empty;

        /// <summary>Label customer (satu atau gabungan jika lebih dari satu).</summary>
        public string CustomerLabel { get; set; } = string.Empty;

        /// <summary>Status keseluruhan grup: Scheduled, In Progress, Prepared, Completed.</summary>
        public string GroupStatus { get; set; } = "Scheduled";

        /// <summary>Total KBN target (agregat semua jadwal dalam grup).</summary>
        public int TotalKbnTarget { get; set; }

        /// <summary>Total KBN yang sudah discan (agregat semua jadwal dalam grup).</summary>
        public int TotalKbnActual { get; set; }

        /// <summary>Daftar jadwal individual yang menjadi anggota grup ini.</summary>
        public List<DeliverySchedule> Schedules { get; set; } = new();

        /// <summary>ScheduleId dari jadwal pertama (representatif) dalam grup.</summary>
        public int RepresentativeScheduleId => Schedules.FirstOrDefault()?.ScheduleId ?? 0;

        /// <summary>Progress persentase KBN.</summary>
        public double ProgressPercent => TotalKbnTarget > 0 ? (double)TotalKbnActual / TotalKbnTarget * 100 : 0;
    }
}
