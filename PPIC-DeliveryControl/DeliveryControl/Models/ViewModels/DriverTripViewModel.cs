using System;
using System.Collections.Generic;
using System.Linq;

namespace DeliveryControl.Models.ViewModels
{
    public class DriverTripViewModel
    {
        // Unique Identifier for the group (using the first ScheduleId as a reference)
        public int RepresentativeScheduleId { get; set; }
        
        public string CustomerName { get; set; } = "";
        public string Cycle { get; set; } = "";
        public string Route { get; set; } = "";
        public string Area { get; set; } = "";
        
        // Time & Status
        public DateTime? PickupTime { get; set; }
        public DateTime? ETD { get; set; }
        
        // Actual Times (Taken from the first schedule, assumed consistent)
        public DateTime? ActualStartTime { get; set; } 
        public DateTime? ActualEndTime { get; set; }

        // Status Logic
        public string DriverStatus { get; set; } = "Scheduled";
        public string OverallStatus { get; set; } = "Scheduled";

        // Collection of Manifests
        public List<DeliverySchedule> Schedules { get; set; } = new List<DeliverySchedule>();

        // Carry-over flag: jadwal dari hari sebelumnya yang belum selesai
        public bool IsCarryOver { get; set; } = false;
        // Tanggal jadwal untuk carry-over display
        public DateTime? ScheduledDate { get; set; }

        // Display Helpers
        public string ManifestList => string.Join(", ", Schedules.Select(s => s.ScheduleNumber));
        public bool HasArrived => ActualStartTime.HasValue;
        public bool HasDeparted => ActualEndTime.HasValue;
        public int TotalSchedules => Schedules.Count;
    }
}
