using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Models.ViewModels
{
    public class AutoSchedulerSettingViewModel
    {
        [Required(ErrorMessage = "Waktu cutoff wajib diisi")]
        [Display(Name = "Jam Cutoff Auto Generate (HH:mm)")]
        public string CutoffTime { get; set; } = "18:00";
    }
}


