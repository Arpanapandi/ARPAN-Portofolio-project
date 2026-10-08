namespace OeeSystem.Models;

public class MachineDowntimeReason
{
    public string MachineId { get; set; } = string.Empty;
    public Machine? Machine { get; set; }

    public int DowntimeReasonId { get; set; }
    public DowntimeReason? DowntimeReason { get; set; }
}



