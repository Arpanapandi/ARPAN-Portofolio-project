using System;
using Microsoft.Data.Sqlite;

class Program
{
    static void Main()
    {
        string connectionString = @"Data Source=C:\DeliveryControl_backup_old\DeliveryControl\delivery_control_local.db";
        using (var connection = new SqliteConnection(connectionString))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "DELETE FROM DeliveryItems; DELETE FROM DeliverySchedules; DELETE FROM PreparationRecords; DELETE FROM PullingRecords;";
                int rows = command.ExecuteNonQuery();
                Console.WriteLine($"Deleted {rows} rows from schedule-related tables.");
            }
        }
    }
}
