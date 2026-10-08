namespace OeeSystem.Models;

public class DowntimeReason
{
    public int Id { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public ICollection<DowntimeEvent> DowntimeEvents { get; set; } = new List<DowntimeEvent>();

    // Mapping downtime reason ke mesin yang diizinkan
    public ICollection<MachineDowntimeReason> MachineDowntimeReasons { get; set; } = new List<MachineDowntimeReason>();
}


