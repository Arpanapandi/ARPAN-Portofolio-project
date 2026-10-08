using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using System.Collections.Generic;

namespace MigrateData {
    class Program {
        static async Task Main() {
            var sqlServerConn = "Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true;MultipleActiveResultSets=true";
            var sqliteConn = @"Data Source=c:\DeliveryControl_backup_old\DeliveryControl\local.db";

            var sqlOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlServer(sqlServerConn)
                .Options;

            var sqliteOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(sqliteConn)
                .Options;

            using var sqlContext = new ApplicationDbContext(sqlOptions);
            using var sqliteContext = new ApplicationDbContext(sqliteOptions);

            Console.WriteLine("Creating SQLite database schema...");
            sqliteContext.Database.EnsureDeleted();
            sqliteContext.Database.EnsureCreated();

            Console.WriteLine("Disabling foreign key constraints for bulk insert...");
            var conn = sqliteContext.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA foreign_keys = OFF;";
            await cmd.ExecuteNonQueryAsync();

            await CopyTable(sqlContext.SystemSettings, sqliteContext.SystemSettings, "SystemSettings", sqliteContext);
            await CopyTable(sqlContext.Users, sqliteContext.Users, "Users", sqliteContext);
            await CopyTable(sqlContext.Customers, sqliteContext.Customers, "Customers", sqliteContext);
            await CopyTable(sqlContext.Docks, sqliteContext.Docks, "Docks", sqliteContext);
            await CopyTable(sqlContext.Items, sqliteContext.Items, "Items", sqliteContext);
            await CopyTable(sqlContext.ScheduleTemplates, sqliteContext.ScheduleTemplates, "ScheduleTemplates", sqliteContext);
            await CopyTable(sqlContext.UserDockAccesses, sqliteContext.UserDockAccesses, "UserDockAccesses", sqliteContext);
            await CopyTable(sqlContext.UserDocks, sqliteContext.UserDocks, "UserDocks", sqliteContext);
            await CopyTable(sqlContext.ScheduleTemplateMappings, sqliteContext.ScheduleTemplateMappings, "ScheduleTemplateMappings", sqliteContext);
            await CopyTable(sqlContext.ItemRackLocations, sqliteContext.ItemRackLocations, "ItemRackLocations", sqliteContext);
            await CopyTable(sqlContext.DeliverySchedules, sqliteContext.DeliverySchedules, "DeliverySchedules", sqliteContext);
            await CopyTable(sqlContext.StockSnapshots, sqliteContext.StockSnapshots, "StockSnapshots", sqliteContext);
            await CopyTable(sqlContext.ScanNGLogs, sqliteContext.ScanNGLogs, "ScanNGLogs", sqliteContext);
            await CopyTable(sqlContext.SmartImportHeaderMaps, sqliteContext.SmartImportHeaderMaps, "SmartImportHeaderMaps", sqliteContext);
            await CopyTable(sqlContext.ItemMappings, sqliteContext.ItemMappings, "ItemMappings", sqliteContext);
            await CopyTable(sqlContext.DeliveryItems, sqliteContext.DeliveryItems, "DeliveryItems", sqliteContext);
            await CopyTable(sqlContext.PullingRecords, sqliteContext.PullingRecords, "PullingRecords", sqliteContext);
            await CopyTable(sqlContext.PreparationRecords, sqliteContext.PreparationRecords, "PreparationRecords", sqliteContext);
            await CopyTable(sqlContext.ActivityLogs, sqliteContext.ActivityLogs, "ActivityLogs", sqliteContext);

            Console.WriteLine("Enabling foreign key constraints...");
            cmd.CommandText = "PRAGMA foreign_keys = ON;";
            await cmd.ExecuteNonQueryAsync();

            Console.WriteLine("Migration completed successfully!");
        }

        static async Task CopyTable<T>(DbSet<T> sourceSet, DbSet<T> targetSet, string tableName, ApplicationDbContext targetContext) where T : class
        {
            Console.WriteLine("Reading $tableName from SQL Server...");
            var data = await sourceSet.AsNoTracking().ToListAsync();
            Console.WriteLine("$tableName: found ${data.Count} records. Inserting into SQLite...");
            
            int batchSize = 1000;
            for (int i = 0; i < data.Count; i += batchSize)
            {
                var batch = data.Skip(i).Take(batchSize).ToList();
                targetSet.AddRange(batch);
                try {
                    await targetContext.SaveChangesAsync();
                } catch (Exception ex) {
                    Console.WriteLine("Error inserting batch in $tableName: ${ex.InnerException?.Message ?? ex.Message}");
                }
                
                foreach (var entry in targetContext.ChangeTracker.Entries().ToList())
                {
                    entry.State = EntityState.Detached;
                }
            }
            Console.WriteLine("$tableName copied.");
        }
    }
}
