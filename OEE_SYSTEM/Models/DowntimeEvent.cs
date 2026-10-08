namespace OeeSystem.Models;

public class DowntimeEvent
{
    public int Id { get; set; }

    public int JobRunId { get; set; }
    public JobRun? JobRun { get; set; }

    public int ReasonId { get; set; }
    public DowntimeReason? Reason { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }

    // Calculated, but stored for reporting convenience
    public double DurationSeconds { get; set; }
}


