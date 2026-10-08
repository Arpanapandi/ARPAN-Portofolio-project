using System;
using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Models
{
    /// <summary>
    /// Model untuk registrasi keywords header Excel agar import lebih fleksibel per customer
    /// </summary>
    public class SmartImportHeaderMap
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Nama Customer (misal: "ADM", "AHM", dsb). 
        /// Gunakan "GENERIC" untuk mapping default semua customer.
        /// </summary>
        [Required]
        [StringLength(50)]
        public string CustomerName { get; set; } = string.Empty;

        /// <summary>
        /// Nama Field Target (Manifest, PartNo, PartName, Qty)
        /// </summary>
        [Required]
        [StringLength(50)]
        public string TargetField { get; set; } = string.Empty;

        /// <summary>
        /// Keywords header di Excel (koma dipisahkan, case-insensitive)
        /// Misal: "Order No, Manifest, DN"
        /// </summary>
        [Required]
        public string HeaderKeywords { get; set; } = string.Empty;

        public DateTime CreatedDate { get; set; } = DateTime.Now;
    }
}
