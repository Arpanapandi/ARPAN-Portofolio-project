namespace OeeSystem.Models;

public class JobRun
{
    public int Id { get; set; }

    public string MachineId { get; set; } = string.Empty;
    public Machine? Machine { get; set; }

    public int WorkOrderId { get; set; }
    public WorkOrder? WorkOrder { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    public int OperatorId { get; set; }
    public User? Operator { get; set; }

    public int? ManPowerId { get; set; }
    public ManPower? ManPower { get; set; }

    public ICollection<DowntimeEvent> DowntimeEvents { get; set; } = new List<DowntimeEvent>();
    public ICollection<ProductionCount> ProductionCounts { get; set; } = new List<ProductionCount>();
}


