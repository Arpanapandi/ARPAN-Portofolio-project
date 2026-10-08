using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk data Customer
    /// </summary>
    public class Customer
    {
        [Key]
        public int CustomerId { get; set; }

        [StringLength(20)]
        [Display(Name = "KODE")]
        public string? AutoCode { get; set; }

        [Required(ErrorMessage = "Nama customer wajib diisi")]
        [StringLength(50)]
        [Display(Name = "NAMA CUSTOMER")]
        public string CustomerCode { get; set; } = string.Empty;

        [Required(ErrorMessage = "Dock wajib diisi")]
        [StringLength(200)]
        [Display(Name = "DOCK")]
        public string CustomerName { get; set; } = string.Empty;

        [StringLength(100)]
        [Display(Name = "Route")]
        public string? Route { get; set; }

        [StringLength(50)]
        [Display(Name = "Cycle")]
        public string? Cycle { get; set; }

        [StringLength(100)]
        [Display(Name = "Docking")]
        public string? Docking { get; set; }

        [StringLength(100)]
        [Display(Name = "Pickup")]
        public string? Pickup { get; set; }

        [StringLength(100)]
        [Display(Name = "ETD")]
        public string? ETD { get; set; }

        [StringLength(100)]
        [Display(Name = "Range")]
        public string? Range { get; set; }

        // Method untuk calculate Range otomatis dari ETD - Pickup
        public void CalculateRange()
        {
            if (!string.IsNullOrWhiteSpace(Pickup) && !string.IsNullOrWhiteSpace(ETD))
            {
                // Parse waktu format HH:mm
                if (TimeSpan.TryParse(Pickup, out TimeSpan pickupTime) && 
                    TimeSpan.TryParse(ETD, out TimeSpan etdTime))
                {
                    var difference = etdTime - pickupTime;
                    
                    // Format hasil
                    if (difference.TotalMinutes < 0)
                    {
                        Range = "Invalid Time";
                    }
                    else if (difference.TotalHours >= 1)
                    {
                        var hours = (int)difference.TotalHours;
                        var minutes = difference.Minutes;
                        Range = minutes > 0 ? $"{hours} Jam {minutes} Menit" : $"{hours} Jam";
                    }
                    else
                    {
                        Range = $"{(int)difference.TotalMinutes} Menit";
                    }
                }
                else
                {
                    Range = null;
                }
            }
            else
            {
                Range = null;
            }
        }

        [StringLength(50)]
        [Display(Name = "SKID")]
        public string? SKID { get; set; }

        [StringLength(100)]
        [Display(Name = "Area")]
        public string? Area { get; set; }

        [StringLength(500)]
        [Display(Name = "Remark Order")]
        public string? RemarkOrder { get; set; }

        [Display(Name = "END PREP")]
        public int StdPrepareTime { get; set; }

        [Display(Name = "START PREP")]
        public int StartPrepareTime { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public DateTime? UpdatedDate { get; set; }

        // Navigation properties
        public virtual ICollection<DeliverySchedule> DeliverySchedules { get; set; } = new List<DeliverySchedule>();
    }
}
