using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Hubs;
using DeliveryControl.Services;

var builder = WebApplication.CreateBuilder(args);

// Daftarkan sebagai Windows Service agar otomatis start saat Windows boot
// Sehingga snapshot jam 08:00 tetap berjalan setiap hari termasuk weekend
builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "PPIC DeliveryControl";
});

// Add services to the container.
builder.Services.AddControllersWithViews(options => 
{
    options.Filters.Add<DeliveryControl.Filters.AuthorizeAttribute>();
});

// Add Response Compression
builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
});

// Add SignalR for real-time updates
builder.Services.AddSignalR();

// Add Session untuk authentication
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(30); // Sesi bertahan 30 hari idle
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".DeliveryControl.Session";
    options.Cookie.MaxAge = TimeSpan.FromDays(30); // Membuat cookie persistent (tahan banting setelah browser ditutup)
});

// Add DbContext
// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (connectionString != null && connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseSqlServer(connectionString, sqlOptions => sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(5),
            errorNumbersToAdd: null));
    }
});

// Add HttpContextAccessor (untuk ActivityLogService)
builder.Services.AddHttpContextAccessor();

// Add ActivityLogService
builder.Services.AddScoped<ActivityLogService>();

// Add PreparationSyncService
builder.Services.AddScoped<PreparationSyncService>();

// Add SmartImportService
builder.Services.AddScoped<SmartImportService>();

// Add SAP Integration Service
builder.Services.AddScoped<ISapIntegrationService, DummySapIntegrationService>();

// Add PreparationBackgroundSyncService (Every 5 minutes)
builder.Services.AddHostedService<PreparationBackgroundSyncService>();

// Add StockSnapshotService (background service cutoff jam 08:00)
builder.Services.AddSingleton<StockSnapshotService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<StockSnapshotService>());

// Add IMemoryCache untuk cache GetStockViewModel (mengurangi beban FIFO calculation berulang)
builder.Services.AddMemoryCache();

// Add StockCacheService untuk invalidasi cache lintas controller
builder.Services.AddSingleton<DeliveryControl.Services.StockCacheService>();

var app = builder.Build();

// Initialize database dengan seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        var connectionString = app.Configuration.GetConnectionString("DefaultConnection") ?? "";
        
        // Pastikan database sudah di-migrate / created
        if (connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                context.Database.Migrate();
                logger.LogInformation("Database migration completed successfully.");
            }
            catch (Exception migrateEx)
            {
                logger.LogWarning(migrateEx, "Migration warning (database might already be up to date).");
            }
        }
        else
        {
            logger.LogInformation("SQL Server connection detected. Skipping EF Core Migrate() to prevent SQLite migration conflicts. Relying on manual EnsureCreated logic.");
        }

        // Enable WAL mode for SQLite to allow concurrent reads and writes
        if (connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                context.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
                logger.LogInformation("SQLite WAL mode enabled.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to enable SQLite WAL mode.");
            }
        }

        // Ensure Remark column exists (safe idempotent)
        try 
        { 
            string sqlAddColumn = connectionString.Contains("Server=") 
                ? "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PullingRecords') AND name = 'Remark') ALTER TABLE PullingRecords ADD Remark NVARCHAR(MAX) DEFAULT 'Match'"
                : "ALTER TABLE PullingRecords ADD COLUMN Remark TEXT DEFAULT 'Match'";
            context.Database.ExecuteSqlRaw(sqlAddColumn); 
            logger.LogInformation("Ensured Remark column in PullingRecords."); 
        } catch (Exception ex) { logger.LogInformation("PullingRecords Remark: {Msg}", ex.Message); }

        try 
        { 
            string sqlAddColumn = connectionString.Contains("Server=") 
                ? "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PreparationRecords') AND name = 'Remark') ALTER TABLE PreparationRecords ADD Remark NVARCHAR(MAX) DEFAULT 'Match'"
                : "ALTER TABLE PreparationRecords ADD COLUMN Remark TEXT DEFAULT 'Match'";
            context.Database.ExecuteSqlRaw(sqlAddColumn); 
            logger.LogInformation("Ensured Remark column in PreparationRecords."); 
        } catch (Exception ex) { logger.LogInformation("PreparationRecords Remark: {Msg}", ex.Message); }

        // Ensure ScanNGLogs table exists
        try
        {
            string sqlCreateTable = connectionString.Contains("Server=")
                ? @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ScanNGLogs') AND type in (N'U'))
                    CREATE TABLE ScanNGLogs (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        Module NVARCHAR(100) NOT NULL DEFAULT '',
                        Tag NVARCHAR(100) NOT NULL DEFAULT '',
                        Label NVARCHAR(100) NOT NULL DEFAULT '',
                        Kanban NVARCHAR(100) NOT NULL DEFAULT '',
                        Reason NVARCHAR(500) NOT NULL DEFAULT '',
                        CreatedBy NVARCHAR(100) NOT NULL DEFAULT '',
                        CreatedDate DATETIME NOT NULL DEFAULT (GETDATE())
                    )"
                : @"CREATE TABLE IF NOT EXISTS ScanNGLogs (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Module TEXT NOT NULL DEFAULT '',
                        Tag TEXT NOT NULL DEFAULT '',
                        Label TEXT NOT NULL DEFAULT '',
                        Kanban TEXT NOT NULL DEFAULT '',
                        Reason TEXT NOT NULL DEFAULT '',
                        CreatedBy TEXT NOT NULL DEFAULT '',
                        CreatedDate TEXT NOT NULL DEFAULT (datetime('now','localtime'))
                    )";
            context.Database.ExecuteSqlRaw(sqlCreateTable);
            logger.LogInformation("ScanNGLogs table ensured.");
        }
        catch (Exception ex) { logger.LogInformation("ScanNGLogs: {Msg}", ex.Message); }

        // Ensure UserDocks table exists (for user-to-dock mapping)
        try
        {
            string sqlUserDocks = connectionString.Contains("Server=")
                ? @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('UserDocks') AND type in (N'U'))
                    CREATE TABLE UserDocks (
                        UserDockId INT IDENTITY(1,1) PRIMARY KEY,
                        UserId INT NOT NULL,
                        CustomerId INT NOT NULL,
                        CONSTRAINT FK_UserDocks_Users_UserId FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
                        CONSTRAINT FK_UserDocks_Customers_CustomerId FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId) ON DELETE CASCADE
                    )"
                : @"CREATE TABLE IF NOT EXISTS UserDocks (
                        UserDockId INTEGER PRIMARY KEY AUTOINCREMENT,
                        UserId INTEGER NOT NULL,
                        CustomerId INTEGER NOT NULL,
                        FOREIGN KEY (UserId) REFERENCES Users(UserId) ON DELETE CASCADE,
                        FOREIGN KEY (CustomerId) REFERENCES Customers(CustomerId) ON DELETE CASCADE
                    )";
            context.Database.ExecuteSqlRaw(sqlUserDocks);
            logger.LogInformation("UserDocks table ensured.");
        }
        catch (Exception ex) { logger.LogInformation("UserDocks: {Msg}", ex.Message); }

        // Ensure ItemRackLocations table exists (multi-rack per item)
        try
        {
            string sqlRackLoc = connectionString.Contains("Server=")
                ? @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ItemRackLocations') AND type in (N'U'))
                    CREATE TABLE ItemRackLocations (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        ItemId INT NOT NULL,
                        Rack NVARCHAR(10) NOT NULL DEFAULT '',
                        NoRack INT NOT NULL DEFAULT 0,
                        Plant NVARCHAR(20) NULL,
                        SortOrder INT NOT NULL DEFAULT 0,
                        CONSTRAINT FK_ItemRackLocations_Items FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE CASCADE
                    )"
                : @"CREATE TABLE IF NOT EXISTS ItemRackLocations (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ItemId INTEGER NOT NULL,
                        Rack TEXT NOT NULL DEFAULT '',
                        NoRack INTEGER NOT NULL DEFAULT 0,
                        Plant TEXT NULL,
                        SortOrder INTEGER NOT NULL DEFAULT 0,
                        FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE CASCADE
                    )";
            context.Database.ExecuteSqlRaw(sqlRackLoc);
            logger.LogInformation("ItemRackLocations table ensured.");
        }
        catch (Exception ex) { logger.LogInformation("ItemRackLocations: {Msg}", ex.Message); }

        // Ensure ShoppingRecords table exists
        try
        {
            string sqlShopping = connectionString.Contains("Server=")
                ? @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ShoppingRecords') AND type in (N'U'))
                    CREATE TABLE ShoppingRecords (
                        ShoppingId INT IDENTITY(1,1) PRIMARY KEY,
                        ItemId INT NULL,
                        Tag NVARCHAR(100) NOT NULL,
                        Label NVARCHAR(100) NOT NULL,
                        Kanban NVARCHAR(100) NOT NULL,
                        CreatedDate DATETIME NOT NULL DEFAULT (GETDATE()),
                        CreatedBy NVARCHAR(100) NULL,
                        Plant NVARCHAR(20) NULL,
                        TargetCustomer NVARCHAR(100) NULL
                    )"
                : @"CREATE TABLE IF NOT EXISTS ShoppingRecords (
                        ShoppingId INTEGER PRIMARY KEY AUTOINCREMENT,
                        ItemId INTEGER NULL,
                        Tag TEXT NOT NULL,
                        Label TEXT NOT NULL,
                        Kanban TEXT NOT NULL,
                        CreatedDate TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                        CreatedBy TEXT NULL,
                        Plant TEXT NULL,
                        TargetCustomer TEXT NULL
                    )";
            context.Database.ExecuteSqlRaw(sqlShopping);
            logger.LogInformation("ShoppingRecords table ensured.");
        }
        catch (Exception ex) { logger.LogInformation("ShoppingRecords: {Msg}", ex.Message); }

        // Ensure PremiumFreights table exists and has all columns
        try
        {
            string sqlPremiumFreights = connectionString.Contains("Server=")
                ? @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('PremiumFreights') AND type in (N'U'))
                    CREATE TABLE PremiumFreights (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        ScheduleId INT NULL,
                        FreightNumber NVARCHAR(100) NULL,
                        KategoriPengiriman NVARCHAR(100) NOT NULL,
                        JenisArmada NVARCHAR(100) NOT NULL,
                        VehicleNumber NVARCHAR(100) NULL,
                        DriverName NVARCHAR(100) NULL,
                        CustomerName NVARCHAR(100) NULL,
                        Route NVARCHAR(100) NULL,
                        Cycle NVARCHAR(50) NULL,
                        ScheduledDate DATETIME NULL,
                        Keterangan NVARCHAR(500) NULL,
                        Status NVARCHAR(50) NOT NULL DEFAULT 'Draft',
                        CreatedDate DATETIME NOT NULL DEFAULT (GETDATE()),
                        CreatedBy NVARCHAR(100) NULL
                    );
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PremiumFreights') AND name = 'ScheduleId') ALTER TABLE PremiumFreights ADD ScheduleId INT NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PremiumFreights') AND name = 'VehicleNumber') ALTER TABLE PremiumFreights ADD VehicleNumber NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PremiumFreights') AND name = 'DriverName') ALTER TABLE PremiumFreights ADD DriverName NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PremiumFreights') AND name = 'CustomerName') ALTER TABLE PremiumFreights ADD CustomerName NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PremiumFreights') AND name = 'Route') ALTER TABLE PremiumFreights ADD Route NVARCHAR(100) NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PremiumFreights') AND name = 'Cycle') ALTER TABLE PremiumFreights ADD Cycle NVARCHAR(50) NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PremiumFreights') AND name = 'ScheduledDate') ALTER TABLE PremiumFreights ADD ScheduledDate DATETIME NULL;
                    IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PremiumFreights') AND name = 'Keterangan') ALTER TABLE PremiumFreights ADD Keterangan NVARCHAR(500) NULL;"
                : @"CREATE TABLE IF NOT EXISTS PremiumFreights (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        ScheduleId INTEGER NULL,
                        FreightNumber TEXT NULL,
                        KategoriPengiriman TEXT NOT NULL,
                        JenisArmada TEXT NOT NULL,
                        VehicleNumber TEXT NULL,
                        DriverName TEXT NULL,
                        CustomerName TEXT NULL,
                        Route TEXT NULL,
                        Cycle TEXT NULL,
                        ScheduledDate TEXT NULL,
                        Keterangan TEXT NULL,
                        Status TEXT NOT NULL DEFAULT 'Draft',
                        CreatedDate TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                        CreatedBy TEXT NULL
                    );";
            context.Database.ExecuteSqlRaw(sqlPremiumFreights);
            logger.LogInformation("PremiumFreights table ensured.");
        }
        catch (Exception ex) { logger.LogInformation("PremiumFreights: {Msg}", ex.Message); }

        // Ensure ScheduleTemplates & Mappings tables exist (Manual Check due to migration lock)
        try
        {
            string sqlTemplates = connectionString.Contains("Server=")
                ? @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ScheduleTemplates') AND type in (N'U'))
                    CREATE TABLE ScheduleTemplates (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        Name NVARCHAR(100) NOT NULL,
                        Description NVARCHAR(500) NULL,
                        CustomerName NVARCHAR(100) NOT NULL,
                        FileType NVARCHAR(50) NOT NULL,
                        IsActive BIT NOT NULL DEFAULT 1,
                        CreatedDate DATETIME NOT NULL DEFAULT (GETDATE()),
                        CreatedBy NVARCHAR(100) NULL
                    )"
                : @"CREATE TABLE IF NOT EXISTS ScheduleTemplates (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL,
                        Description TEXT NULL,
                        CustomerName TEXT NOT NULL,
                        FileType TEXT NOT NULL,
                        IsActive INTEGER NOT NULL DEFAULT 1,
                        CreatedDate TEXT NOT NULL DEFAULT (datetime('now','localtime')),
                        CreatedBy TEXT NULL
                    )";
            context.Database.ExecuteSqlRaw(sqlTemplates);

            string sqlMappings = connectionString.Contains("Server=")
                ? @"IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID('ScheduleTemplateMappings') AND type in (N'U'))
                    CREATE TABLE ScheduleTemplateMappings (
                        Id INT IDENTITY(1,1) PRIMARY KEY,
                        TemplateId INT NOT NULL,
                        SourceKeywords NVARCHAR(500) NOT NULL,
                        TargetField NVARCHAR(100) NOT NULL,
                        IsHeader BIT NOT NULL DEFAULT 0,
                        ColumnIndex INT NULL,
                        RegexPattern NVARCHAR(MAX) NULL,
                        CONSTRAINT FK_ScheduleTemplateMappings_ScheduleTemplates_TemplateId FOREIGN KEY (TemplateId) REFERENCES ScheduleTemplates(Id) ON DELETE CASCADE
                    )
                    ELSE
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('ScheduleTemplateMappings') AND name = 'RegexPattern')
                        ALTER TABLE ScheduleTemplateMappings ADD RegexPattern NVARCHAR(MAX) NULL
                    END"
                : @"CREATE TABLE IF NOT EXISTS ScheduleTemplateMappings (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        TemplateId INTEGER NOT NULL,
                        SourceKeywords TEXT NOT NULL,
                        TargetField TEXT NOT NULL,
                        IsHeader INTEGER NOT NULL DEFAULT 0,
                        ColumnIndex INTEGER NULL,
                        RegexPattern TEXT NULL,
                        FOREIGN KEY (TemplateId) REFERENCES ScheduleTemplates(Id) ON DELETE CASCADE
                    )";
            context.Database.ExecuteSqlRaw(sqlMappings);
            logger.LogInformation("Schedule Templates tables ensured.");
        }
        catch (Exception ex) { logger.LogInformation("ScheduleTemplates: {Msg}", ex.Message); }

        // Ensure RackAlt columns in Items table (for alternate rack auto-routing)
        try
        {
            bool isSqlServer = connectionString.Contains("Server=");
            string[] rackAltCols = isSqlServer
                ? new[]
                {
                    "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Items') AND name = 'RackAlt') ALTER TABLE Items ADD RackAlt NVARCHAR(10) NULL",
                    "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Items') AND name = 'NoRackAlt') ALTER TABLE Items ADD NoRackAlt INT NULL",
                    "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Items') AND name = 'RackAltCapacity') ALTER TABLE Items ADD RackAltCapacity INT NULL",
                    "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Items') AND name = 'IsDeleted') ALTER TABLE Items ADD IsDeleted BIT NOT NULL DEFAULT 0"
                }
                : new[]
                {
                    "ALTER TABLE Items ADD COLUMN RackAlt TEXT NULL",
                    "ALTER TABLE Items ADD COLUMN NoRackAlt INTEGER NULL",
                    "ALTER TABLE Items ADD COLUMN RackAltCapacity INTEGER NULL",
                    "ALTER TABLE Items ADD COLUMN IsDeleted INTEGER NOT NULL DEFAULT 0"
                };
            foreach (var sql in rackAltCols)
            {
                try { context.Database.ExecuteSqlRaw(sql); }
                catch { /* kolom sudah ada, abaikan */ }
            }
            logger.LogInformation("RackAlt columns in Items ensured.");
        }
        catch (Exception ex) { logger.LogInformation("RackAlt columns: {Msg}", ex.Message); }
        
        // Ensure DockMapping columns in PreparationRecords (for dock-aware scan preparation)
        try
        {
            bool isSqlServer2 = connectionString.Contains("Server=");
            string[] dockCols = isSqlServer2
                ? new[]
                {
                    "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PreparationRecords') AND name = 'DockName') ALTER TABLE PreparationRecords ADD DockName NVARCHAR(200) NULL",
                    "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PreparationRecords') AND name = 'DockCycle') ALTER TABLE PreparationRecords ADD DockCycle NVARCHAR(50) NULL",
                    "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PreparationRecords') AND name = 'DockCustomerId') ALTER TABLE PreparationRecords ADD DockCustomerId INT NULL"
                }
                : new[]
                {
                    "ALTER TABLE PreparationRecords ADD COLUMN DockName TEXT NULL",
                    "ALTER TABLE PreparationRecords ADD COLUMN DockCycle TEXT NULL",
                    "ALTER TABLE PreparationRecords ADD COLUMN DockCustomerId INTEGER NULL"
                };
            foreach (var sql in dockCols)
            {
                try { context.Database.ExecuteSqlRaw(sql); }
                catch (Exception colEx) { logger.LogDebug("DockMapping col: {Msg}", colEx.Message); }
            }
            logger.LogInformation("DockMapping columns in PreparationRecords ensured.");
        }
        catch (Exception ex) { logger.LogInformation("DockMapping columns: {Msg}", ex.Message); }

        // Ensure TargetDate column in PreparationRecords (for future-dated scan preparation)
        try
        {
            bool isSqlServerTD = connectionString.Contains("Server=");
            string sqlTargetDate = isSqlServerTD
                ? "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('PreparationRecords') AND name = 'TargetDate') ALTER TABLE PreparationRecords ADD TargetDate DATETIME NULL"
                : "ALTER TABLE PreparationRecords ADD COLUMN TargetDate TEXT NULL";
            try { context.Database.ExecuteSqlRaw(sqlTargetDate); }
            catch { /* kolom sudah ada, abaikan */ }
            logger.LogInformation("TargetDate column in PreparationRecords ensured.");
        }
        catch (Exception ex) { logger.LogInformation("TargetDate column: {Msg}", ex.Message); }

        // Ensure PrepScanTime column in DeliveryItems (for scan history visibility)
        try
        {
            bool isSqlServer3 = connectionString.Contains("Server=");
            string sqlPrepTime = isSqlServer3
                ? "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryItems') AND name = 'PrepScanTime') ALTER TABLE DeliveryItems ADD PrepScanTime DATETIME NULL"
                : "ALTER TABLE DeliveryItems ADD COLUMN PrepScanTime TEXT NULL";
            try { context.Database.ExecuteSqlRaw(sqlPrepTime); }
            catch { /* kolom sudah ada, abaikan */ }
            logger.LogInformation("PrepScanTime column in DeliveryItems ensured.");
        }
        catch (Exception ex) { logger.LogInformation("PrepScanTime column: {Msg}", ex.Message); }

        // Ensure QtyLotOverride column in DeliveryItems
        try
        {
            bool isSqlServerQty = connectionString.Contains("Server=");
            string sqlQtyLot = isSqlServerQty
                ? "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryItems') AND name = 'QtyLotOverride') ALTER TABLE DeliveryItems ADD QtyLotOverride INT NULL"
                : "ALTER TABLE DeliveryItems ADD COLUMN QtyLotOverride INTEGER NULL";
            try { context.Database.ExecuteSqlRaw(sqlQtyLot); }
            catch { /* kolom sudah ada, abaikan */ }
            logger.LogInformation("QtyLotOverride column in DeliveryItems ensured.");
        }
        catch (Exception ex) { logger.LogInformation("QtyLotOverride column: {Msg}", ex.Message); }

        // Ensure ExternalPartNo column in DeliveryItems (simpan Part No asli dari manifest)
        try
        {
            bool isSqlServerExt = connectionString.Contains("Server=");
            string sqlExtPartNo = isSqlServerExt
                ? "IF NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('DeliveryItems') AND name = 'ExternalPartNo') ALTER TABLE DeliveryItems ADD ExternalPartNo NVARCHAR(100) NULL"
                : "ALTER TABLE DeliveryItems ADD COLUMN ExternalPartNo TEXT NULL";
            try { context.Database.ExecuteSqlRaw(sqlExtPartNo); }
            catch { /* kolom sudah ada, abaikan */ }
            logger.LogInformation("ExternalPartNo column in DeliveryItems ensured.");

            // Backfill ExternalPartNo untuk data lama (yang belum punya nilai)
            try
            {
                string sqlBackfill = isSqlServerExt
                    ? @"UPDATE di SET di.ExternalPartNo = COALESCE(NULLIF(i.CustomerPartNumber,''), i.ItemCode)
                        FROM DeliveryItems di
                        INNER JOIN Items i ON di.ItemId = i.ItemId
                        WHERE (di.ExternalPartNo IS NULL OR di.ExternalPartNo = '')"
                    : @"UPDATE DeliveryItems SET ExternalPartNo = (
                            SELECT COALESCE(NULLIF(i.CustomerPartNumber,''), i.ItemCode)
                            FROM Items i WHERE i.ItemId = DeliveryItems.ItemId
                        ) WHERE (ExternalPartNo IS NULL OR ExternalPartNo = '')";
                context.Database.ExecuteSqlRaw(sqlBackfill);
                logger.LogInformation("ExternalPartNo backfill for old data completed.");
            }
            catch (Exception bfEx) { logger.LogInformation("ExternalPartNo backfill: {Msg}", bfEx.Message); }
        }
        catch (Exception ex) { logger.LogInformation("ExternalPartNo column: {Msg}", ex.Message); }

        // Ensure Performance Indexes
        try
        {
            if (connectionString.Contains("Server="))
            {
                string sqlIndexes = @"
                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DeliverySchedules_ScheduledDate_Status' AND object_id = OBJECT_ID('DeliverySchedules'))
                        CREATE NONCLUSTERED INDEX IX_DeliverySchedules_ScheduledDate_Status ON DeliverySchedules(ScheduledDate, Status) INCLUDE (CustomerId, Area, Cycle, Route);

                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DeliveryItems_ScheduleId_ItemId' AND object_id = OBJECT_ID('DeliveryItems'))
                        CREATE NONCLUSTERED INDEX IX_DeliveryItems_ScheduleId_ItemId ON DeliveryItems(ScheduleId, ItemId);

                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_Items_VIN_ItemCode' AND object_id = OBJECT_ID('Items'))
                        CREATE NONCLUSTERED INDEX IX_Items_VIN_ItemCode ON Items(VIN, ItemCode, IsDeleted, IsActive);

                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PullingRecords_Tag_Label' AND object_id = OBJECT_ID('PullingRecords'))
                        CREATE NONCLUSTERED INDEX IX_PullingRecords_Tag_Label ON PullingRecords(Tag, Label, CreatedDate);

                    IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_PreparationRecords_Tag_Label' AND object_id = OBJECT_ID('PreparationRecords'))
                        CREATE NONCLUSTERED INDEX IX_PreparationRecords_Tag_Label ON PreparationRecords(Tag, Label, CreatedDate);
                ";
                context.Database.ExecuteSqlRaw(sqlIndexes);
                logger.LogInformation("Performance indexes ensured.");
            }
        }
        catch (Exception ex) { logger.LogInformation("Performance indexes: {Msg}", ex.Message); }

        // Seed Users
        DeliveryControl.Data.DbInitializer.Initialize(context);

        // DATA FIX: Pastikan item yang Tidak Aktif memiliki IsActive = false
        var wrongItems = context.Items.Where(i => i.StatusItem == "Tidak Aktif" && i.IsActive).ToList();
        if (wrongItems.Any())
        {
            foreach (var item in wrongItems)
            {
                item.IsActive = false;
            }
            context.SaveChanges();
            logger.LogInformation($"Fixed {wrongItems.Count} items that were incorrectly marked as active despite being Tidak Aktif.");
        }

        // DATA FIX: Pastikan item yang Reguler, PMSP, PMSP FM, PMSP SM, After Market, atau No Order memiliki IsActive = true
        var wrongInactiveItems = context.Items.Where(i =>
            (i.StatusItem == "Reguler" || i.StatusItem == "PMSP" ||
             i.StatusItem == "PMSP FM" || i.StatusItem == "PMSP SM" ||
             i.StatusItem == "After Market" ||
             i.StatusItem == "No Order" || i.StatusItem == "NO ORDER") && !i.IsActive).ToList();
        if (wrongInactiveItems.Any())
        {
            foreach (var item in wrongInactiveItems)
            {
                item.IsActive = true;
            }
            context.SaveChanges();
            logger.LogInformation($"Fixed {wrongInactiveItems.Count} items that were incorrectly marked as inactive despite being {wrongInactiveItems.FirstOrDefault()?.StatusItem}.");
        }

        // DATA FIX: Normalize Plant "MOLDED" to "Molded"
        try 
        {
            context.Database.ExecuteSqlRaw("UPDATE Items SET Plant = 'Molded' WHERE Plant = 'MOLDED'");
            context.Database.ExecuteSqlRaw("UPDATE PullingRecords SET Plant = 'Molded' WHERE Plant = 'MOLDED'");
            context.Database.ExecuteSqlRaw("UPDATE PreparationRecords SET Plant = 'Molded' WHERE Plant = 'MOLDED'");
            context.Database.ExecuteSqlRaw("UPDATE ItemRackLocations SET Plant = 'Molded' WHERE Plant = 'MOLDED'");
            logger.LogInformation("Fixed Plant case MOLDED -> Molded.");
        }
        catch (Exception ex) { logger.LogInformation("Failed to normalize Plant: " + ex.Message); }

        logger.LogInformation("Database initialization completed.");

        // Manual seeding trigger via CLI argument: --seed-stock
        if (args.Contains("--seed-stock"))
        {
            logger.LogInformation("Flag --seed-stock detected. Seeding historical data...");
            DeliveryControl.Data.DbInitializer.SeedHistoricalStockSnapshots(context);
            logger.LogInformation("Historical data seeding completed.");
        }

        // Manual migration trigger via CLI argument: --migrate-sqlite
        if (args.Contains("--migrate-sqlite"))
        {
            logger.LogInformation("Flag --migrate-sqlite detected. Starting data migration to SQLite...");
            await DeliveryControl.MigrateDataToSqlite.RunAsync(services);
            logger.LogInformation("Data migration completed successfully.");
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while seeding the database.");
        // Jangan stop aplikasi jika seed data gagal
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseResponseCompression();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

// Map SignalR Hub
app.MapHub<DeliveryHub>("/deliveryHub");
app.MapHub<StockHub>("/stockHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
