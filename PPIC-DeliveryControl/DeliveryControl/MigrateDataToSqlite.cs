using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DeliveryControl
{
    public static class MigrateDataToSqlite
    {
        public static async Task RunAsync(IServiceProvider serviceProvider)
        {
            var logger = serviceProvider.GetRequiredService<ILogger<ApplicationDbContext>>();
            var config = serviceProvider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
            
            string sqlConnectionStr = config.GetConnectionString("ProductionSqlConnection");
            string sqliteConnectionStr = config.GetConnectionString("DefaultConnection");
            
            logger.LogInformation("Starting Data Migration...");
            
            var sqlOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(sqlConnectionStr)
                .Options;
                
            var sqliteOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(sqliteConnectionStr)
                .Options;
                
            using var sourceDb = new ApplicationDbContext(sqlOptions);
            using var destDb = new ApplicationDbContext(sqliteOptions);
            
            // Turn off FK checks for SQLite
            await destDb.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF;");
            
            // Migrate tables
            await CopyTable(sourceDb.Users, destDb.Users, destDb, logger, "Users");
            await CopyTable(sourceDb.Customers, destDb.Customers, destDb, logger, "Customers");
            await CopyTable(sourceDb.SystemSettings, destDb.SystemSettings, destDb, logger, "SystemSettings");
            await CopyTable(sourceDb.Docks, destDb.Docks, destDb, logger, "Docks");
            await CopyTable(sourceDb.ScheduleTemplates, destDb.ScheduleTemplates, destDb, logger, "ScheduleTemplates");
            await CopyTable(sourceDb.Items, destDb.Items, destDb, logger, "Items");
            await CopyTable(sourceDb.ItemMappings, destDb.ItemMappings, destDb, logger, "ItemMappings");
            await CopyTable(sourceDb.UserDocks, destDb.UserDocks, destDb, logger, "UserDocks");
            await CopyTable(sourceDb.UserDockAccesses, destDb.UserDockAccesses, destDb, logger, "UserDockAccesses");
            await CopyTable(sourceDb.ItemRackLocations, destDb.ItemRackLocations, destDb, logger, "ItemRackLocations");
            await CopyTable(sourceDb.DeliverySchedules, destDb.DeliverySchedules, destDb, logger, "DeliverySchedules");
            await CopyTable(sourceDb.DeliveryItems, destDb.DeliveryItems, destDb, logger, "DeliveryItems");
            await CopyTable(sourceDb.ScheduleTemplateMappings, destDb.ScheduleTemplateMappings, destDb, logger, "ScheduleTemplateMappings");
            await CopyTable(sourceDb.ActivityLogs, destDb.ActivityLogs, destDb, logger, "ActivityLogs");
            await CopyTable(sourceDb.PullingRecords, destDb.PullingRecords, destDb, logger, "PullingRecords");
            await CopyTable(sourceDb.PreparationRecords, destDb.PreparationRecords, destDb, logger, "PreparationRecords");
            await CopyTable(sourceDb.StockSnapshots, destDb.StockSnapshots, destDb, logger, "StockSnapshots");
            await CopyTable(sourceDb.ScanNGLogs, destDb.ScanNGLogs, destDb, logger, "ScanNGLogs");
            await CopyTable(sourceDb.SmartImportHeaderMaps, destDb.SmartImportHeaderMaps, destDb, logger, "SmartImportHeaderMaps");
            
            await destDb.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = ON;");
            logger.LogInformation("Data Migration Completed!");
        }
        
        private static async Task CopyTable<T>(DbSet<T> source, DbSet<T> dest, ApplicationDbContext destDb, ILogger logger, string tableName) where T : class
        {
            logger.LogInformation($"Copying {tableName}...");
            try {
                var records = await source.AsNoTracking().ToListAsync();
                if (records.Count > 0)
                {
                    if (await dest.AnyAsync()) {
                        logger.LogInformation($"Clearing existing data in {tableName}...");
                        dest.RemoveRange(await dest.ToListAsync());
                        await destDb.SaveChangesAsync();
                        destDb.ChangeTracker.Clear();
                    }
                    await dest.AddRangeAsync(records);
                    await destDb.SaveChangesAsync();
                    destDb.ChangeTracker.Clear();
                    logger.LogInformation($"Copied {records.Count} records to {tableName}.");
                }
            }
            catch (Exception ex)
            {
                destDb.ChangeTracker.Clear();
                logger.LogError(ex, $"Failed to copy {tableName}");
            }
        }
    }
}
