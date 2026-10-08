using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Models;

namespace dashboardKlinik.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }

        public DbSet<Pasien> Pasien { get; set; }
        public DbSet<KunjunganKlinik> KunjunganKlinik { get; set; }
        public DbSet<ActivityLog> ActivityLog { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<AntrianKlinik> AntrianKlinik { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Index for AntrianKlinik
            modelBuilder.Entity<AntrianKlinik>()
                .HasIndex(a => a.Tanggal);

            modelBuilder.Entity<AntrianKlinik>()
                .HasIndex(a => a.StatusAntrian);

            modelBuilder.Entity<AntrianKlinik>()
                .HasIndex(a => a.NPK);

            // Index for User
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();

            modelBuilder.Entity<User>()
                .HasIndex(u => u.NPK);

            // Index for Pasien
            modelBuilder.Entity<Pasien>()
                .HasIndex(p => p.NPK)
                .IsUnique();

            modelBuilder.Entity<Pasien>()
                .HasIndex(p => p.NamaPasien);

            modelBuilder.Entity<Pasien>()
                .HasIndex(p => p.Plant);

            modelBuilder.Entity<Pasien>()
                .HasIndex(p => p.Departemen);

            // Index for KunjunganKlinik
            modelBuilder.Entity<KunjunganKlinik>()
                .HasIndex(k => k.TanggalKunjungan);

            modelBuilder.Entity<KunjunganKlinik>()
                .HasIndex(k => k.NPK);

            modelBuilder.Entity<KunjunganKlinik>()
                .HasIndex(k => k.Status);

            // Relationship
            modelBuilder.Entity<KunjunganKlinik>()
                .HasOne(k => k.Pasien)
                .WithMany(p => p.Kunjungan)
                .HasForeignKey(k => k.PasienId)
                .OnDelete(DeleteBehavior.SetNull);

            // Index for ActivityLog
            modelBuilder.Entity<ActivityLog>()
                .HasIndex(a => a.Timestamp);
        }
    }
}
