namespace OeeSystem.Models;

public enum WorkOrderStatus
{
    Planned,
    InProgress,
    Completed
}

public class WorkOrder
{
    public int Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    public int TargetQuantity { get; set; }
    public WorkOrderStatus Status { get; set; } = WorkOrderStatus.Planned;
    
    // Planning fields
    public DateTime? PlannedDate { get; set; }
    public int? ShiftId { get; set; }
    public Shift? Shift { get; set; }

    public ICollection<JobRun> JobRuns { get; set; } = new List<JobRun>();
}


