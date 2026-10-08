using Microsoft.EntityFrameworkCore;
using OeeSystem.Models;

namespace OeeSystem.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Machine> Machines => Set<Machine>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductMachine> ProductMachines => Set<ProductMachine>();
    public DbSet<ProductNgType> ProductNgTypes => Set<ProductNgType>();
    public DbSet<DowntimeReason> DowntimeReasons => Set<DowntimeReason>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<User> Users => Set<User>();
    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();
    public DbSet<JobRun> JobRuns => Set<JobRun>();
    public DbSet<DowntimeEvent> DowntimeEvents => Set<DowntimeEvent>();
    public DbSet<ProductionCount> ProductionCounts => Set<ProductionCount>();
    public DbSet<MachineDowntimeReason> MachineDowntimeReasons => Set<MachineDowntimeReason>();
    public DbSet<Plant> Plants => Set<Plant>();
    public DbSet<NgType> NgTypes => Set<NgType>();
    public DbSet<ManPower> ManPowers => Set<ManPower>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Machine.Id as varchar(4)
        modelBuilder.Entity<Machine>()
            .Property(m => m.Id)
            .HasMaxLength(4)
            .IsRequired();

        // Configure MachineId foreign keys as varchar(4)
        modelBuilder.Entity<JobRun>()
            .Property(j => j.MachineId)
            .HasMaxLength(4)
            .IsRequired();

        modelBuilder.Entity<ProductMachine>()
            .Property(pm => pm.MachineId)
            .HasMaxLength(4)
            .IsRequired();

        modelBuilder.Entity<MachineDowntimeReason>()
            .Property(md => md.MachineId)
            .HasMaxLength(4)
            .IsRequired();

        // Simpan enum sebagai string supaya cocok dengan script INSERT yang diberikan
        modelBuilder.Entity<User>()
            .Property(u => u.Role)
            .HasConversion<string>();

        // Custom conversion untuk MachineStatus dengan backward compatibility
        modelBuilder.Entity<Machine>()
            .Property(m => m.Status)
            .HasConversion(
                v => v.ToString(), // Convert enum to string
                v => ConvertMachineStatus(v)); // Convert string to enum dengan backward compatibility

        modelBuilder.Entity<WorkOrder>()
            .Property(w => w.Status)
            .HasConversion<string>();

        // Relasi dasar (sebagian besar mengikuti konvensi, ini hanya eksplisitkan yang penting)
        modelBuilder.Entity<JobRun>()
            .HasOne(j => j.Machine)
            .WithMany(m => m.JobRuns)
            .HasForeignKey(j => j.MachineId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<JobRun>()
            .HasOne(j => j.WorkOrder)
            .WithMany(w => w.JobRuns)
            .HasForeignKey(j => j.WorkOrderId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<JobRun>()
            .HasOne(j => j.Operator)
            .WithMany(o => o.JobRuns)
            .HasForeignKey(j => j.OperatorId);

        modelBuilder.Entity<JobRun>()
            .HasOne(j => j.ManPower)
            .WithMany()
            .HasForeignKey(j => j.ManPowerId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<DowntimeEvent>()
            .HasOne(d => d.JobRun)
            .WithMany(j => j.DowntimeEvents)
            .HasForeignKey(d => d.JobRunId);

        modelBuilder.Entity<DowntimeEvent>()
            .HasOne(d => d.Reason)
            .WithMany(r => r.DowntimeEvents)
            .HasForeignKey(d => d.ReasonId);

        modelBuilder.Entity<ProductionCount>()
            .HasOne(p => p.JobRun)
            .WithMany(j => j.ProductionCounts)
            .HasForeignKey(p => p.JobRunId);

        // ✅ Configure InjectionGroup column
        modelBuilder.Entity<ProductionCount>()
            .Property(p => p.InjectionGroup)
            .HasMaxLength(50);

        modelBuilder.Entity<ProductionCount>()
            .HasOne(p => p.NgType)
            .WithMany()
            .HasForeignKey(p => p.NgTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        // Relasi many-to-many Product <-> Machine melalui ProductMachine
        modelBuilder.Entity<ProductMachine>()
            .HasKey(pm => new { pm.ProductId, pm.MachineId });

        // Hindari multiple cascade paths: gunakan DeleteBehavior.Restrict
        modelBuilder.Entity<ProductMachine>()
            .HasOne(pm => pm.Product)
            .WithMany(p => p.ProductMachines)
            .HasForeignKey(pm => pm.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProductMachine>()
            .HasOne(pm => pm.Machine)
            .WithMany(m => m.ProductMachines)
            .HasForeignKey(pm => pm.MachineId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relasi many-to-many Machine <-> DowntimeReason melalui MachineDowntimeReason
        modelBuilder.Entity<MachineDowntimeReason>()
            .HasKey(md => new { md.MachineId, md.DowntimeReasonId });

        modelBuilder.Entity<MachineDowntimeReason>()
            .HasOne(md => md.Machine)
            .WithMany(m => m.MachineDowntimeReasons)
            .HasForeignKey(md => md.MachineId);

        modelBuilder.Entity<MachineDowntimeReason>()
            .HasOne(md => md.DowntimeReason)
            .WithMany(r => r.MachineDowntimeReasons)
            .HasForeignKey(md => md.DowntimeReasonId);

        // Relasi many-to-many Product <-> NgType melalui ProductNgType
        modelBuilder.Entity<ProductNgType>()
            .HasKey(pn => new { pn.ProductId, pn.NgTypeId });

        modelBuilder.Entity<ProductNgType>()
            .HasOne(pn => pn.Product)
            .WithMany(p => p.ProductNgTypes)
            .HasForeignKey(pn => pn.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ProductNgType>()
            .HasOne(pn => pn.NgType)
            .WithMany(n => n.ProductNgTypes)
            .HasForeignKey(pn => pn.NgTypeId)
            .OnDelete(DeleteBehavior.Restrict);

        // ========== DATA DUMMY PLANT & MACHINE ==========
        // Plant wajib ada karena Machine memiliki FK PlantId
        // ✅ PERBAIKAN: Hapus HasData untuk Machine karena seeding sudah di-handle di Program.cs
        // HasData() akan selalu mencoba insert data saat migration, menyebabkan data lama muncul kembali
        modelBuilder.Entity<Plant>().HasData(
            new Plant { Id = 1, Code = "PLT01", Name = "Plant Dummy" }
        );

        // ✅ PERBAIKAN: Seeding Machine dipindahkan ke Program.cs dengan conditional check
        // Ini mencegah data lama muncul kembali setelah dihapus
        // modelBuilder.Entity<Machine>().HasData(...) - DIHAPUS
    }
    
    // Helper method untuk convert string status ke enum dengan backward compatibility
    private static MachineStatus ConvertMachineStatus(string status)
    {
        return status switch
        {
            "Aktif" => MachineStatus.Aktif,
            "TidakAktif" => MachineStatus.TidakAktif,
            // Backward compatibility: convert status lama ke status baru
            "Running" => MachineStatus.Aktif,
            "Idle" => MachineStatus.Aktif,
            "NoLoading" => MachineStatus.Aktif,
            "Down" => MachineStatus.TidakAktif,
            _ => MachineStatus.TidakAktif // Default jika tidak dikenal
        };
    }
}


