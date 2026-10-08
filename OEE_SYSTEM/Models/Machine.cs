namespace OeeSystem.Models;

public enum MachineStatus
{
    Aktif,
    TidakAktif
}

public class Machine
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string LineId { get; set; } = string.Empty;
    public int PlantId { get; set; }
    public Plant? Plant { get; set; }
    public MachineStatus Status { get; set; } = MachineStatus.TidakAktif;
    public string? ImageUrl { get; set; }  // Path/URL gambar mesin (disimpan di database)
    
    public DateTime? ManufacturingYear { get; set; }  // Tahun Mesin
    public DateTime? InstallationYear { get; set; }   // Tahun Instalasi
    public string? Description { get; set; }          // Keterangan

    public ICollection<JobRun> JobRuns { get; set; } = new List<JobRun>();

    // Mapping mesin ke produk yang diizinkan
    public ICollection<ProductMachine> ProductMachines { get; set; } = new List<ProductMachine>();

    // Mapping mesin ke downtime reason yang diizinkan
    public ICollection<MachineDowntimeReason> MachineDowntimeReasons { get; set; } = new List<MachineDowntimeReason>();
}


