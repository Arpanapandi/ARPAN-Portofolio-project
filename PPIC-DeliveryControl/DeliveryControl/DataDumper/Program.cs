using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using System.Text;

// Configuration
var sqliteDbPath = @"D:\16. Digitalisasi\1. Project\2026\Delivery\DeliveryControl\DeliveryControl\DeliveryControl\DeliveryControl.db";
var sqlServerConnStr = "Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true";

// Tables to migrate (in order to respect FK constraints)
var tables = new[] {
    "Users",
    "Customers", 
    "Items",
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
    "StockSnapshots"
};

Console.WriteLine("=== SQLite to SQL Server Migration ===");
Console.WriteLine($"Source: {sqliteDbPath}");
Console.WriteLine($"Target: SQL Server");
Console.WriteLine();

using var sqliteConn = new SqliteConnection($"Data Source={sqliteDbPath}");
sqliteConn.Open();

using var sqlServerConn = new SqlConnection(sqlServerConnStr);
sqlServerConn.Open();

foreach (var table in tables)
{
    try
    {
        MigrateTable(sqliteConn, sqlServerConn, table);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  ERROR: {ex.Message}");
    }
}

Console.WriteLine("\n=== Migration Complete ===");

void MigrateTable(SqliteConnection sqlite, SqlConnection sqlServer, string tableName)
{
    Console.Write($"Migrating {tableName}... ");
    
    // Get column info from SQLite
    var columns = new List<string>();
    using (var cmd = sqlite.CreateCommand())
    {
        cmd.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }
    }
    
    if (columns.Count == 0)
    {
        Console.WriteLine("SKIP (no columns)");
        return;
    }
    
    // Read data from SQLite
    var rows = new List<object?[]>();
    using (var cmd = sqlite.CreateCommand())
    {
        cmd.CommandText = $"SELECT * FROM {tableName}";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < reader.FieldCount; i++)
            {
                row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            }
            rows.Add(row);
        }
    }
    
    if (rows.Count == 0)
    {
        Console.WriteLine("SKIP (empty)");
        return;
    }
    
    // Check which columns exist in SQL Server
    var sqlServerColumns = new List<string>();
    using (var cmd = sqlServer.CreateCommand())
    {
        cmd.CommandText = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}' ORDER BY ORDINAL_POSITION";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            sqlServerColumns.Add(reader.GetString(0));
        }
    }
    
    // Find common columns
    var commonColumns = columns.Where(c => sqlServerColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
    var columnIndices = commonColumns.Select(c => columns.IndexOf(c)).ToList();
    
    if (commonColumns.Count == 0)
    {
        Console.WriteLine("SKIP (no matching columns)");
        return;
    }
    
    // Check if table has identity column (usually first column ending with Id)
    var hasIdentity = commonColumns.Any(c => c.EndsWith("Id", StringComparison.OrdinalIgnoreCase));
    
    // Enable identity insert if needed
    if (hasIdentity)
    {
        using var setIdentityOn = sqlServer.CreateCommand();
        setIdentityOn.CommandText = $"SET IDENTITY_INSERT [{tableName}] ON";
        try { setIdentityOn.ExecuteNonQuery(); } catch { }
    }
    
    // Insert rows
    int inserted = 0;
    int skipped = 0;
    
    foreach (var row in rows)
    {
        try
        {
            var values = columnIndices.Select(i => row[i]).ToArray();
            var paramNames = commonColumns.Select((c, i) => $"@p{i}").ToList();
            
            var insertSql = $"INSERT INTO [{tableName}] ([{string.Join("], [", commonColumns)}]) VALUES ({string.Join(", ", paramNames)})";
            
            using var insertCmd = sqlServer.CreateCommand();
            insertCmd.CommandText = insertSql;
            
            for (int i = 0; i < values.Length; i++)
            {
                var val = values[i];
                
                // Handle type conversions
                if (val is long l) val = (int)l; // SQLite uses long for integers
                if (val is double d && commonColumns[i].Contains("Quantity", StringComparison.OrdinalIgnoreCase))
                    val = (decimal)d;
                if (val is double d2 && (commonColumns[i].Contains("Weight", StringComparison.OrdinalIgnoreCase) || 
                    commonColumns[i].Contains("Volume", StringComparison.OrdinalIgnoreCase)))
                    val = (decimal)d2;
                
                insertCmd.Parameters.AddWithValue($"@p{i}", val ?? DBNull.Value);
            }
            
            insertCmd.ExecuteNonQuery();
            inserted++;
        }
        catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601) // Duplicate key
        {
            skipped++;
        }
    }
    
    // Disable identity insert if needed
    if (hasIdentity)
    {
        using var setIdentityOff = sqlServer.CreateCommand();
        setIdentityOff.CommandText = $"SET IDENTITY_INSERT [{tableName}] OFF";
        try { setIdentityOff.ExecuteNonQuery(); } catch { }
    }
    
    Console.WriteLine($"OK ({inserted} inserted, {skipped} skipped)");
}
