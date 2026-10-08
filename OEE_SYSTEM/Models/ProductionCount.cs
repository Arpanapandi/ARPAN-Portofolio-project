namespace OeeSystem.Models;

public class ProductionCount
{
    public int Id { get; set; }

    public int JobRunId { get; set; }
    public JobRun? JobRun { get; set; }

    public DateTime Timestamp { get; set; }
    public int GoodCount { get; set; }
    public int RejectCount { get; set; }
    public string? RejectReason { get; set; }
    
    public int? NgTypeId { get; set; }
    public NgType? NgType { get; set; }
    
    // ✅ Group Injection: "merah" atau "biru"
    public string? InjectionGroup { get; set; }
}


