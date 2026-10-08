using Microsoft.EntityFrameworkCore;
using SistemPacking.Web.Models;

namespace SistemPacking.Web.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Area> Areas => Set<Area>();
    public DbSet<FGLocation> FGLocations => Set<FGLocation>();
    public DbSet<Item> Items => Set<Item>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<ReasonReject> ReasonRejects => Set<ReasonReject>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderDetail> OrderDetails => Set<OrderDetail>();
    public DbSet<OrderUploadHistory> OrderUploadHistories => Set<OrderUploadHistory>();
    public DbSet<ShoppingLog> ShoppingLogs => Set<ShoppingLog>();
    public DbSet<PackingLog> PackingLogs => Set<PackingLog>();
    public DbSet<Verification> Verifications => Set<Verification>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<ItemMapping> ItemMappings => Set<ItemMapping>();
    public DbSet<ManPower> ManPowers => Set<ManPower>();
    public DbSet<ScanNgLog> ScanNgLogs => Set<ScanNgLog>();

    // Database Views
    // public DbSet<ShoppingLogView> ShoppingLogViews => Set<ShoppingLogView>();
    public DbSet<PackingLogView> PackingLogViews => Set<PackingLogView>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Soft delete filter
        modelBuilder.Entity<User>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Customer>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Item>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<Order>().HasQueryFilter(x => !x.IsDeleted);
        modelBuilder.Entity<OrderDetail>().HasQueryFilter(x => !x.IsDeleted);

        // Database Views Configuration
        /* 
        modelBuilder.Entity<ShoppingLogView>()
            .ToView("View_ShoppingLogs")
            .HasNoKey();
        */
            
        modelBuilder.Entity<PackingLogView>()
            .ToView("View_PackingLogs")
            .HasNoKey();

        // User - Role
        modelBuilder.Entity<User>()
            .HasOne(u => u.Role)
            .WithMany(r => r.Users)
            .HasForeignKey(u => u.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Order - Customer
        modelBuilder.Entity<Order>()
            .HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Order - ReleasedBy (User)
        modelBuilder.Entity<Order>()
            .HasOne(o => o.ReleasedBy)
            .WithMany()
            .HasForeignKey(o => o.ReleasedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Order - CancelledBy (User)
        modelBuilder.Entity<Order>()
            .HasOne(o => o.CancelledBy)
            .WithMany()
            .HasForeignKey(o => o.CancelledById)
            .OnDelete(DeleteBehavior.Restrict);

        // OrderDetail - Order
        modelBuilder.Entity<OrderDetail>()
            .HasOne(od => od.Order)
            .WithMany(o => o.OrderDetails)
            .HasForeignKey(od => od.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // OrderDetail - Item
        modelBuilder.Entity<OrderDetail>()
            .HasOne(od => od.Item)
            .WithMany(i => i.OrderDetails)
            .HasForeignKey(od => od.ItemId)
            .OnDelete(DeleteBehavior.Restrict);

        // Item - Customer
        modelBuilder.Entity<Item>()
            .HasOne(i => i.Customer)
            .WithMany(c => c.Items)
            .HasForeignKey(i => i.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Item - FGLocation
        modelBuilder.Entity<Item>()
            .HasOne(i => i.FGLocation)
            .WithMany(l => l.Items)
            .HasForeignKey(i => i.FGLocationId)
            .OnDelete(DeleteBehavior.SetNull);

        // FGLocation - Area
        modelBuilder.Entity<FGLocation>()
            .HasOne(l => l.Area)
            .WithMany(a => a.Locations)
            .HasForeignKey(l => l.AreaId)
            .OnDelete(DeleteBehavior.Restrict);

        // ShoppingLog - OrderDetail
        modelBuilder.Entity<ShoppingLog>()
            .HasOne(sl => sl.OrderDetail)
            .WithMany(od => od.ShoppingLogs)
            .HasForeignKey(sl => sl.OrderDetailId)
            .OnDelete(DeleteBehavior.Cascade);

        // ShoppingLog - Operator (User)
        modelBuilder.Entity<ShoppingLog>()
            .HasOne(sl => sl.Operator)
            .WithMany()
            .HasForeignKey(sl => sl.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        // PackingLog - OrderDetail
        modelBuilder.Entity<PackingLog>()
            .HasOne(pl => pl.OrderDetail)
            .WithMany(od => od.PackingLogs)
            .HasForeignKey(pl => pl.OrderDetailId)
            .OnDelete(DeleteBehavior.Cascade);

        // PackingLog - Operator (User)
        modelBuilder.Entity<PackingLog>()
            .HasOne(pl => pl.Operator)
            .WithMany()
            .HasForeignKey(pl => pl.OperatorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Verification - Order (1-1)
        modelBuilder.Entity<Verification>()
            .HasOne(v => v.Order)
            .WithOne(o => o.Verification)
            .HasForeignKey<Verification>(v => v.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Verification - Leader (User)
        modelBuilder.Entity<Verification>()
            .HasOne(v => v.Leader)
            .WithMany()
            .HasForeignKey(v => v.LeaderId)
            .OnDelete(DeleteBehavior.Restrict);

        // ActivityLog - User
        modelBuilder.Entity<ActivityLog>()
            .HasOne(al => al.User)
            .WithMany(u => u.ActivityLogs)
            .HasForeignKey(al => al.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        // OrderUploadHistory - UploadedBy
        modelBuilder.Entity<OrderUploadHistory>()
            .HasOne(h => h.UploadedBy)
            .WithMany()
            .HasForeignKey(h => h.UploadedById)
            .OnDelete(DeleteBehavior.Restrict);

        // Order - UploadHistory
        modelBuilder.Entity<Order>()
            .HasOne(o => o.UploadHistory)
            .WithMany(h => h.Orders)
            .HasForeignKey(o => o.UploadHistoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Indexes
        modelBuilder.Entity<User>().HasIndex(u => u.NIK).IsUnique();
        modelBuilder.Entity<User>().HasIndex(u => u.Username).IsUnique();
        modelBuilder.Entity<Item>().HasIndex(i => i.Barcode).IsUnique();
        modelBuilder.Entity<Item>().HasIndex(i => i.ItemCode).IsUnique();
        modelBuilder.Entity<Customer>().HasIndex(c => c.CustomerCode).IsUnique();
        modelBuilder.Entity<Area>().HasIndex(a => a.AreaCode).IsUnique();
        modelBuilder.Entity<FGLocation>().HasIndex(l => l.LocationCode).IsUnique();
        modelBuilder.Entity<Order>().HasIndex(o => o.OrderNo).IsUnique();

        // Seed Data
        SeedData(modelBuilder);
    }

    private static void SeedData(ModelBuilder modelBuilder)
    {
        // Seed Roles
        modelBuilder.Entity<Role>().HasData(
            new Role { Id = 1, RoleName = "Super Admin", Description = "Full access", CanManageMaster = true, CanManageOrder = true, CanShopping = true, CanPacking = true, CanVerify = true, CanViewReport = true, CanManageUser = true, CreatedAt = new DateTime(2026, 1, 1) },
            new Role { Id = 2, RoleName = "Leader", Description = "Leader access", CanManageMaster = false, CanManageOrder = true, CanShopping = false, CanPacking = false, CanVerify = true, CanViewReport = true, CanManageUser = false, CreatedAt = new DateTime(2026, 1, 1) },
            new Role { Id = 3, RoleName = "Operator Shopping", Description = "Shopping operator", CanManageMaster = false, CanManageOrder = false, CanShopping = true, CanPacking = false, CanVerify = false, CanViewReport = false, CanManageUser = false, CreatedAt = new DateTime(2026, 1, 1) },
            new Role { Id = 4, RoleName = "Operator Packing", Description = "Packing operator", CanManageMaster = false, CanManageOrder = false, CanShopping = false, CanPacking = true, CanVerify = false, CanViewReport = false, CanManageUser = false, CreatedAt = new DateTime(2026, 1, 1) },
            new Role { Id = 5, RoleName = "Viewer", Description = "Read-only access", CanManageMaster = false, CanManageOrder = false, CanShopping = false, CanPacking = false, CanVerify = false, CanViewReport = true, CanManageUser = false, CreatedAt = new DateTime(2026, 1, 1) }
        );

        // Seed Default Admin User (Password: Admin@123)
        modelBuilder.Entity<User>().HasData(
            new User
            {
                Id = 1,
                NIK = "ADM001",
                Username = "admin",
                FullName = "Super Administrator",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin@123"),
                RoleId = 1,
                IsActive = true,
                CreatedAt = new DateTime(2026, 1, 1)
            }
        );

        // Seed Default Shifts
        modelBuilder.Entity<Shift>().HasData(
            new Shift { Id = 1, ShiftName = "Shift 1 (Pagi)", StartTime = new TimeSpan(7, 0, 0), EndTime = new TimeSpan(15, 0, 0), IsActive = true, CreatedAt = new DateTime(2026, 1, 1) },
            new Shift { Id = 2, ShiftName = "Shift 2 (Siang)", StartTime = new TimeSpan(15, 0, 0), EndTime = new TimeSpan(23, 0, 0), IsActive = true, CreatedAt = new DateTime(2026, 1, 1) },
            new Shift { Id = 3, ShiftName = "Shift 3 (Malam)", StartTime = new TimeSpan(23, 0, 0), EndTime = new TimeSpan(7, 0, 0), IsActive = true, CreatedAt = new DateTime(2026, 1, 1) }
        );

        // Seed Reason Reject
        modelBuilder.Entity<ReasonReject>().HasData(
            new ReasonReject { Id = 1, ReasonCode = "R001", Description = "Qty tidak sesuai", IsActive = true, CreatedAt = new DateTime(2026, 1, 1) },
            new ReasonReject { Id = 2, ReasonCode = "R002", Description = "Item tidak sesuai", IsActive = true, CreatedAt = new DateTime(2026, 1, 1) },
            new ReasonReject { Id = 3, ReasonCode = "R003", Description = "Packing rusak", IsActive = true, CreatedAt = new DateTime(2026, 1, 1) },
            new ReasonReject { Id = 4, ReasonCode = "R004", Description = "Lainnya", IsActive = true, CreatedAt = new DateTime(2026, 1, 1) }
        );

        // Seed Customer
        modelBuilder.Entity<Customer>().HasData(
            new Customer { Id = 1, CustomerCode = "CUST001", CustomerName = "Default Customer", IsActive = true, CreatedAt = new DateTime(2026, 1, 1) }
        );

        // Seed Items
        modelBuilder.Entity<Item>().HasData(
            new Item { Id = 1, ItemCode = "TA1234", ItemName = "Item TA", Barcode = "LBL-TA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 10, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 2, ItemCode = "NA1234", ItemName = "Item NA", Barcode = "LBL-NA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 20, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 3, ItemCode = "HN1234", ItemName = "Item HN", Barcode = "LBL-HN1234", CustomerId = 1, UOM = "PCS", PackingStandard = 50, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 4, ItemCode = "ZA1234", ItemName = "Item ZA", Barcode = "LBL-ZA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 15, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 5, ItemCode = "YA1234", ItemName = "Item YA", Barcode = "LBL-YA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 25, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 6, ItemCode = "XA1234", ItemName = "Item XA", Barcode = "LBL-XA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 30, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 7, ItemCode = "WA1234", ItemName = "Item WA", Barcode = "LBL-WA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 12, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 8, ItemCode = "VA1234", ItemName = "Item VA", Barcode = "LBL-VA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 8, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 9, ItemCode = "UA1234", ItemName = "Item UA", Barcode = "LBL-UA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 40, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 10, ItemCode = "SA1234", ItemName = "Item SA", Barcode = "LBL-SA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 60, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 11, ItemCode = "RA1234", ItemName = "Item RA", Barcode = "LBL-RA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 10, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 12, ItemCode = "QA1234", ItemName = "Item QA", Barcode = "LBL-QA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 5, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 13, ItemCode = "PA1234", ItemName = "Item PA", Barcode = "LBL-PA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 100, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 14, ItemCode = "OA1234", ItemName = "Item OA", Barcode = "LBL-OA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 50, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 15, ItemCode = "MA1234", ItemName = "Item MA", Barcode = "LBL-MA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 20, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 16, ItemCode = "LA1234", ItemName = "Item LA", Barcode = "LBL-LA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 10, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 17, ItemCode = "KA1234", ItemName = "Item KA", Barcode = "LBL-KA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 15, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 18, ItemCode = "JA1234", ItemName = "Item JA", Barcode = "LBL-JA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 30, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 19, ItemCode = "IA1234", ItemName = "Item IA", Barcode = "LBL-IA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 25, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 20, ItemCode = "GA1234", ItemName = "Item GA", Barcode = "LBL-GA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 18, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 21, ItemCode = "FA1234", ItemName = "Item FA", Barcode = "LBL-FA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 22, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 22, ItemCode = "EA1234", ItemName = "Item EA", Barcode = "LBL-EA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 12, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 23, ItemCode = "DA1234", ItemName = "Item DA", Barcode = "LBL-DA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 14, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 24, ItemCode = "CA1234", ItemName = "Item CA", Barcode = "LBL-CA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 16, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) },
            new Item { Id = 25, ItemCode = "BA1234", ItemName = "Item BA", Barcode = "LBL-BA1234", CustomerId = 1, UOM = "PCS", PackingStandard = 24, Status = ItemStatus.Active, CreatedAt = new DateTime(2026, 1, 1) }
        );

        var additionalItems = new List<Item>();
        for (int i = 26; i <= 60; i++)
        {
            string code = $"DUMMY{i:D4}";
            additionalItems.Add(new Item 
            { 
                Id = i, 
                ItemCode = code, 
                ItemName = $"Item {code}", 
                Barcode = $"LBL-{code}", 
                CustomerId = 1, 
                UOM = "PCS", 
                PackingStandard = 10, 
                Status = ItemStatus.Active, 
                CreatedAt = new DateTime(2026, 1, 1) 
            });
        }
        modelBuilder.Entity<Item>().HasData(additionalItems);
    }
}
