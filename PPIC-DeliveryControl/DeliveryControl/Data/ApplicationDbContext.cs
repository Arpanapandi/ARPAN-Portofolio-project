using Microsoft.EntityFrameworkCore;
using DeliveryControl.Models;

namespace DeliveryControl.Data
{
    /// <summary>
    /// Database Context untuk Delivery Control System
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Item> Items { get; set; }
        public DbSet<Customer> Customers { get; set; }
        public DbSet<DeliverySchedule> DeliverySchedules { get; set; }
        public DbSet<DeliveryItem> DeliveryItems { get; set; }
        public DbSet<ItemMapping> ItemMappings { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Dock> Docks { get; set; }
        public DbSet<UserDockAccess> UserDockAccesses { get; set; }
        public DbSet<SystemSetting> SystemSettings { get; set; }
        public DbSet<ActivityLog> ActivityLogs { get; set; }
        public DbSet<PullingRecord> PullingRecords { get; set; }
        public DbSet<PreparationRecord> PreparationRecords { get; set; }
        public DbSet<UserDock> UserDocks { get; set; }
        public DbSet<StockSnapshot> StockSnapshots { get; set; }
        public DbSet<ScanNGLog> ScanNGLogs { get; set; }
        public DbSet<SmartImportHeaderMap> SmartImportHeaderMaps { get; set; }
        public DbSet<ScheduleTemplate> ScheduleTemplates { get; set; }
        public DbSet<ScheduleTemplateMapping> ScheduleTemplateMappings { get; set; }
        public DbSet<ItemRackLocation> ItemRackLocations { get; set; }
        public DbSet<ShoppingRecord> ShoppingRecords { get; set; }
        public DbSet<PremiumFreight> PremiumFreights { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Customer
            modelBuilder.Entity<Customer>(entity =>
            {
                entity.HasIndex(e => new { 
                    e.CustomerCode, 
                    e.CustomerName, 
                    e.Route, 
                    e.Cycle, 
                    e.Docking, 
                    e.Pickup, 
                    e.ETD, 
                    e.Range, 
                    e.SKID, 
                    e.Area 
                }).IsUnique();
                entity.Property(e => e.CustomerCode).IsRequired();
                entity.Property(e => e.CustomerName).IsRequired();
            });

            // Configure Item
            modelBuilder.Entity<Item>(entity =>
            {
                entity.HasIndex(e => e.ItemCode).IsUnique();
                entity.HasIndex(e => e.VIN); // Index for VIN lookups
                entity.HasIndex(e => e.CreatedDate); // Index for sorting by date
                entity.Property(e => e.ItemCode).IsRequired();
                entity.Property(e => e.ItemName).IsRequired();
            });

            // Configure ItemRackLocation
            modelBuilder.Entity<ItemRackLocation>(entity =>
            {
                entity.HasOne(r => r.Item)
                    .WithMany(i => i.RackLocations)
                    .HasForeignKey(r => r.ItemId)
                    .OnDelete(DeleteBehavior.Cascade);
                entity.HasIndex(e => new { e.ItemId, e.Rack, e.NoRack });
            });

            // Configure DeliverySchedule
            modelBuilder.Entity<DeliverySchedule>(entity =>
            {
                entity.HasIndex(e => e.ScheduleNumber);
                
                entity.HasOne(ds => ds.Customer)
                    .WithMany(c => c.DeliverySchedules)
                    .HasForeignKey(ds => ds.CustomerId)
                    .OnDelete(DeleteBehavior.SetNull);

                // Index untuk pencarian berdasarkan tanggal dan status
                entity.HasIndex(e => e.ScheduledDate);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedDate); // Index for sorting
                entity.HasIndex(e => new { e.CustomerId, e.ScheduledDate });
            });

            // Configure DeliveryItem
            modelBuilder.Entity<DeliveryItem>(entity =>
            {
                entity.HasOne(di => di.DeliverySchedule)
                    .WithMany(ds => ds.DeliveryItems)
                    .HasForeignKey(di => di.ScheduleId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(di => di.Item)
                    .WithMany(i => i.DeliveryItems)
                    .HasForeignKey(di => di.ItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => new { e.ScheduleId, e.ItemId });
            });

            // Configure PreparationRecord
            modelBuilder.Entity<PreparationRecord>(entity =>
            {
                entity.ToTable("PreparationRecords");
                entity.HasOne(pr => pr.DeliverySchedule)
                    .WithMany(ds => ds.PreparationRecords)
                    .HasForeignKey(pr => pr.ScheduleId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.Tag);
                entity.HasIndex(e => e.Label);
                entity.HasIndex(e => e.CreatedDate);
                entity.HasIndex(e => new { e.Tag, e.Label }); // For Composite FIFO lookups
            });

            // Configure PullingRecord
            modelBuilder.Entity<PullingRecord>(entity =>
            {
                entity.ToTable("PullingRecords");
                entity.HasOne(pr => pr.Item)
                    .WithMany(i => i.PullingRecords)
                    .HasForeignKey(pr => pr.ItemId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasIndex(e => e.Tag);
                entity.HasIndex(e => e.Label);
                entity.HasIndex(e => e.CreatedDate);
                entity.HasIndex(e => new { e.Tag, e.Label }); // For Composite FIFO lookups
            });

            // Configure User
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasIndex(e => e.Username).IsUnique();
                entity.Property(e => e.Username).IsRequired();
                entity.Property(e => e.Password).IsRequired();
                entity.Property(e => e.FullName).IsRequired();
                entity.Property(e => e.Role).IsRequired();
            });

            // Configure UserDockAccess
            modelBuilder.Entity<UserDockAccess>(entity =>
            {
                entity.HasIndex(e => new { e.UserId, e.DockId }).IsUnique();
                
                entity.HasOne(uda => uda.User)
                    .WithMany(u => u.UserDockAccesses)
                    .HasForeignKey(uda => uda.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                entity.HasOne(uda => uda.Dock)
                    .WithMany()
                    .HasForeignKey(uda => uda.DockId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure ScheduleTemplate
            modelBuilder.Entity<ScheduleTemplate>(entity =>
            {
                entity.HasIndex(e => e.Name);
                entity.HasIndex(e => e.CustomerName);
            });

            // Configure ScheduleTemplateMapping
            modelBuilder.Entity<ScheduleTemplateMapping>(entity =>
            {
                entity.HasOne(m => m.Template)
                    .WithMany(t => t.Mappings)
                    .HasForeignKey(m => m.TemplateId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Seed Data (Optional - untuk development)
            SeedData(modelBuilder);
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            // Seed Customers
            modelBuilder.Entity<Customer>().HasData(
                new Customer
                {
                    CustomerId = 1,
                    CustomerCode = "CUST001",
                    CustomerName = "PT ABC Manufacturing",
                    Route = "Route A",
                    SKID = "10 SKID",
                    IsActive = true,
                    CreatedDate = DateTime.Now
                },
                new Customer
                {
                    CustomerId = 2,
                    CustomerCode = "CUST002",
                    CustomerName = "PT XYZ Industries",
                    Route = "Route B",
                    SKID = "15 SKID",
                    IsActive = true,
                    CreatedDate = DateTime.Now
                }
            );

            // Seed Items
            modelBuilder.Entity<Item>().HasData(
                new Item
                {
                    ItemId = 1,
                    ItemCode = "ITM001",
                    ItemName = "Raw Material A",
                    Description = "Raw material untuk produksi",
                    Unit = "KG",
                    Category = "Raw Material",
                    Weight = 1.0m,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                },
                new Item
                {
                    ItemId = 2,
                    ItemCode = "ITM002",
                    ItemName = "Finished Product B",
                    Description = "Produk jadi siap kirim",
                    Unit = "PCS",
                    Category = "Finished Goods",
                    Weight = 2.5m,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                },
                new Item
                {
                    ItemId = 3,
                    ItemCode = "ITM003",
                    ItemName = "Packaging Material",
                    Description = "Material packaging",
                    Unit = "BOX",
                    Category = "Packaging",
                    Weight = 0.5m,
                    IsActive = true,
                    CreatedDate = DateTime.Now
                }
            );

            // Configure SystemSetting
            modelBuilder.Entity<SystemSetting>(entity =>
            {
                entity.HasIndex(e => e.Key).IsUnique();
                entity.Property(e => e.Key).IsRequired();
                entity.Property(e => e.Value).IsRequired();
            });

            // Configure ActivityLog
            modelBuilder.Entity<ActivityLog>(entity =>
            {
                entity.HasIndex(e => e.Timestamp);
                entity.HasIndex(e => e.Module);
                entity.HasIndex(e => new { e.Module, e.Action });
                entity.Property(e => e.Module).IsRequired();
                entity.Property(e => e.Action).IsRequired();
                entity.Property(e => e.PerformedBy).IsRequired();
            });

            // Note: User seed data akan dibuat via migration atau manual
            // Default users:
            // - Username: admin, Password: admin123, Role: Admin
            // - Username: user, Password: user123, Role: User

            // Configure StockSnapshot
            modelBuilder.Entity<StockSnapshot>(entity =>
            {
                entity.HasIndex(e => new { e.Plant, e.SnapshotDate });
                entity.HasIndex(e => e.SnapshotDate);
                entity.HasIndex(e => new { e.ItemCode, e.SnapshotDate });
            });

            // Configure UserDock
            modelBuilder.Entity<UserDock>(entity =>
            {
                entity.HasOne(e => e.User)
                    .WithMany()
                    .HasForeignKey(e => e.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
                
                entity.HasOne(e => e.Customer)
                    .WithMany()
                    .HasForeignKey(e => e.CustomerId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure ScanNGLog
            modelBuilder.Entity<ScanNGLog>(entity =>
            {
                entity.HasIndex(e => e.Module);
                entity.HasIndex(e => e.CreatedDate);
                entity.HasIndex(e => new { e.Module, e.CreatedDate });
                entity.HasIndex(e => e.CreatedBy);
            });
        }
    }
}

