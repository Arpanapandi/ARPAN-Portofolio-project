using OeeSystem.Models;

namespace OeeSystem.Models.ViewModels;

public class CreateWorkOrderViewModel
{
    // Step 1: Pilih Plant dulu, lalu Mesin
    public int? PlantId { get; set; }
    public string? MachineId { get; set; }
    
    // Step 2: Multiple Schedules untuk mesin yang dipilih
    public List<WorkOrderScheduleItem> Schedules { get; set; } = new();
    
    // Untuk display di dropdown
    public List<Plant> Plants { get; set; } = new();
    public List<Machine> Machines { get; set; } = new();
    public List<Product> Products { get; set; } = new();
    public List<Shift> Shifts { get; set; } = new();
    public List<User> Operators { get; set; } = new();
    public List<ManPower> ManPowers { get; set; } = new();
}

public class WorkOrderScheduleItem
{
    public int ProductId { get; set; }
    public DateTime PlannedDate { get; set; } = DateTime.Today;
    public int ShiftId { get; set; }
    public int TargetQuantity { get; set; }
    public int? OperatorId { get; set; }
    public int? ManPowerId { get; set; }
}

