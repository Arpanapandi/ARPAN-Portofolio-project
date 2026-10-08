using OeeSystem.Models;

namespace OeeSystem.Models.ViewModels;

public class OperatorViewModel
{
    public string MachineId { get; set; } = string.Empty;
    public string MachineName { get; set; } = string.Empty;
    public string? OperatorName { get; set; }

    public string? ProductName { get; set; }
    public string? ProductImageUrl { get; set; }
    public string? WorkOrderNumber { get; set; }
    public int TargetQuantity { get; set; }

    public int TotalGood { get; set; }
    public int TotalReject { get; set; }

    public bool HasActiveJob { get; set; }
    public bool HasActiveDowntime { get; set; }
    public string? ActiveDowntimeDescription { get; set; }

    public TimeSpan SinceLastChange { get; set; }

    // Machine Status dari Admin (Aktif/Tidak Aktif)
    public MachineStatus MachineStatus { get; set; } = MachineStatus.TidakAktif;

    public List<DowntimeReason> LineStopReasons { get; set; } = new();
    public DowntimeReason? RestReason { get; set; }
    public List<NgType> NgTypes { get; set; } = new();
}


