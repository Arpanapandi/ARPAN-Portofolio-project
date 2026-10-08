using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System;

var sqlServerConnStr = "Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true;MultipleActiveResultSets=true;Connection Timeout=120;";
var sqliteDbPath = @"c:\DeliveryControl_backup1\DeliveryControl\DeliveryControl.db";

var tables = new[] {
    "Users",
    "Customers", 
    "Items",
    "ItemRackLocations",
    "ScheduleTemplates",
    "ScheduleTemplateMappings",
    "ItemMappings",
    "Docks",
    "DeliverySchedules",
    "DeliveryItems",
    "PreparationRecords",
    "PullingRecords",
    "SystemSettings",
    "ActivityLogs",
    "UserDocks",
    "UserDockAccesses",
    "StockSnapshots",
    "ScanNGLogs"
};

Console.WriteLine("=== SQL Server to SQLite Migration ===");

using var sqliteConn = new SqliteConnection($"Data Source={sqliteDbPath}");
sqliteConn.Open();

using (var fkCmd = sqliteConn.CreateCommand())
{
    fkCmd.CommandText = "PRAGMA foreign_keys = OFF;";
    fkCmd.ExecuteNonQuery();
}

foreach (var table in tables)
{
    try
    {
        MigrateTable(sqlServerConnStr, sqliteConn, table);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ERROR on table {table}: {ex.Message}");
    }
}

using (var fkCmd2 = sqliteConn.CreateCommand())
{
    fkCmd2.CommandText = "PRAGMA foreign_keys = ON;";
    fkCmd2.ExecuteNonQuery();
}

Console.WriteLine("\n=== Migration Complete ===");

void MigrateTable(string sqlServerStr, SqliteConnection sqlite, string tableName)
{
    Console.Write($"Migrating {tableName}... ");
    
    using var checkCmd = sqlite.CreateCommand();
    checkCmd.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}'";
    if (checkCmd.ExecuteScalar() == null)
    {
        Console.WriteLine("SKIP (table doesn't exist in SQLite)");
        return;
    }

    using var delCmd = sqlite.CreateCommand();
    delCmd.CommandText = $"DELETE FROM {tableName}";
    delCmd.ExecuteNonQuery();

    var sqliteColumns = new List<string>();
    using (var cmd = sqlite.CreateCommand())
    {
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) sqliteColumns.Add(reader.GetString(1));
    }
    
    using var sqlServerConn = new SqlConnection(sqlServerStr);
    sqlServerConn.Open();

    var sqlServerColumns = new List<string>();
    using (var cmd = sqlServerConn.CreateCommand())
    {
        cmd.CommandText = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}'";
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) sqlServerColumns.Add(reader.GetString(0));
    }
    
    var commonColumns = sqliteColumns.Where(c => sqlServerColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    if (commonColumns.Count == 0) 
    {
        Console.WriteLine("SKIP (no matching columns)");
        return;
    }

    using var selCmd = sqlServerConn.CreateCommand();
    selCmd.CommandText = $"SELECT [{string.Join("], [", commonColumns)}] FROM [{tableName}]";
    selCmd.CommandTimeout = 120;
    using var readerData = selCmd.ExecuteReader();
    
    int inserted = 0;
    
    using var transaction = sqlite.BeginTransaction();
    try 
    {
        while (readerData.Read())
        {
            var paramNames = commonColumns.Select((c, i) => $"@p{i}");
            var insertSql = $"INSERT OR IGNORE INTO {tableName} ({string.Join(", ", commonColumns)}) VALUES ({string.Join(", ", paramNames)})";
            
            using var insertCmd = sqlite.CreateCommand();
            insertCmd.CommandText = insertSql;
            insertCmd.Transaction = transaction;
            
            for (int i = 0; i < commonColumns.Count; i++)
            {
                var val = readerData.IsDBNull(i) ? null : readerData.GetValue(i);
                if (val is bool b) val = b ? 1 : 0;
                insertCmd.Parameters.AddWithValue($"@p{i}", val ?? DBNull.Value);
            }
            
            insertCmd.ExecuteNonQuery();
            inserted++;
        }
        transaction.Commit();
        Console.WriteLine($"OK ({inserted} inserted)");
    }
    catch
    {
        transaction.Rollback();
        throw;
    }
}
