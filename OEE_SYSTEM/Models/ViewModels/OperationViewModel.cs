using OeeSystem.Models;

namespace OeeSystem.Models.ViewModels;

public class OperationViewModel
{
    // Untuk dropdown
    public List<Machine> Machines { get; set; } = new();
    public List<WorkOrder> WorkOrders { get; set; } = new();
    public List<User> Operators { get; set; } = new();
    public List<DowntimeReason> DowntimeReasons { get; set; } = new();
    public List<NgType> NgTypes { get; set; } = new(); // Added for ProductionCount form

    // Nilai default / terpilih
    public string? SelectedMachineId { get; set; }
    public int? SelectedJobRunId { get; set; }
}


