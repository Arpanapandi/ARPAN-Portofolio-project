using OeeSystem.Models;

namespace OeeSystem.Models.ViewModels;

public class MachineCardViewModel
{
    public string MachineId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string LineId { get; set; } = string.Empty;
    public MachineStatus Status { get; set; }
    public string? ProductName { get; set; }
    public string? ProductImageUrl { get; set; }
    public string? WorkOrderNumber { get; set; }
}

public class DashboardViewModel
{
    public DateTime CurrentTime { get; set; }
    public string CurrentShiftName { get; set; } = string.Empty;
    public int? SelectedShiftId { get; set; }
    public List<ShiftOption> Shifts { get; set; } = new();

    // Filter PLANT dan Mesin
    public int? SelectedPlantId { get; set; }
    public string? SelectedMachineId { get; set; }
    public List<PlantOption> Plants { get; set; } = new();
    public List<MachineOption> MachineOptions { get; set; } = new();

    public double Availability { get; set; }
    public double Performance { get; set; }
    public double Quality { get; set; }
    public double Oee { get; set; }

    public List<MachineCardViewModel> Machines { get; set; } = new();
    public List<MachineStateTimelineViewModel> MachineStateTimelines { get; set; } = new();
}

public class ShiftOption
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TimeSpan StartTime { get; set; }
    public TimeSpan EndTime { get; set; }
}

public class PlantOption
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class MachineOption
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int PlantId { get; set; }
}

public class MachineStateTimelineViewModel
{
    public string MachineId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public List<string> TimeLabels { get; set; } = new();
    public List<StateSegmentData> StateData { get; set; } = new();
}

public class StateSegmentData
{
    public string State { get; set; } = string.Empty; // "Run", "Stop", "Idle"
    public string StartTime { get; set; } = string.Empty; // Format: "HH:mm"
    public string EndTime { get; set; } = string.Empty; // Format: "HH:mm"
    public double DurationMinutes { get; set; }
}


