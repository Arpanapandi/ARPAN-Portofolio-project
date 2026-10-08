using Microsoft.Data.Sqlite;

var dbPath = @"C:\DeliveryControl\DeliveryControl\DeliveryControl.db";
Console.WriteLine($"Connecting to: {dbPath}");

using var conn = new SqliteConnection($"Data Source={dbPath}");
conn.Open();
Console.WriteLine("Connected!");

var columns = new[]
{
    ("IsLeaderVerified",           "INTEGER NOT NULL DEFAULT 0"),
    ("LeaderVerifiedAt",           "TEXT NULL"),
    ("LeaderVerifiedBy",           "TEXT NULL"),
    ("LeaderVerifiedKanbanCount",  "INTEGER NOT NULL DEFAULT 0"),
};

foreach (var (col, type) in columns)
{
    try
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"ALTER TABLE DeliverySchedules ADD COLUMN {col} {type}";
        cmd.ExecuteNonQuery();
        Console.WriteLine($"OK Added: {col}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"SKIP {col}: {ex.Message}");
    }
}

// Register migration in EF history
try
{
    using var cmd = conn.CreateCommand();
    cmd.CommandText = @"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) 
                        VALUES ('20260226000001_AddLeaderVerificationFields', '8.0.0')";
    cmd.ExecuteNonQuery();
    Console.WriteLine("Migration history updated.");
}
catch (Exception ex)
{
    Console.WriteLine($"History error: {ex.Message}");
}

Console.WriteLine("DONE!");
