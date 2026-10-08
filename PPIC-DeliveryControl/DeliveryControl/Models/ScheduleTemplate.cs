using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk menyimpan template konfigurasi import jadwal per customer (Dynamic Import)
    /// </summary>
    public class ScheduleTemplate
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [Display(Name = "Nama Template")]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Customer")]
        [StringLength(100)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Tipe File")]
        [StringLength(50)]
        public string FileType { get; set; } = "Excel"; // Excel, PDF, Image

        [Display(Name = "Deskripsi")]
        public string? Description { get; set; }

        [Display(Name = "Aktif")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Tanggal Buat")]
        public DateTime CreatedDate { get; set; } = DateTime.Now;

        public string? CreatedBy { get; set; }

        public virtual ICollection<ScheduleTemplateMapping> Mappings { get; set; } = new List<ScheduleTemplateMapping>();
    }
}
