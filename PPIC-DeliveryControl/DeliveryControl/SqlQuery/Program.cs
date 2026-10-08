using System;
using Microsoft.Data.SqlClient;

class SqlQueryProgram
{
    static void Run()
    {
        string connStr = "Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true";
        using (var connection = new SqlConnection(connStr))
        {
            connection.Open();
            var command = connection.CreateCommand();
            
            command.CommandText = "SELECT COUNT(DISTINCT VIN) FROM Items WHERE IsActive=1 AND (StatusItem != 'No Order' OR StatusItem IS NULL)";
            Console.WriteLine($"Distinct Active VINs: {command.ExecuteScalar()}");
            
            command.CommandText = "SELECT COUNT(*) FROM Items WHERE IsActive=1 AND (StatusItem != 'No Order' OR StatusItem IS NULL)";
            Console.WriteLine($"Total Active Item Records: {command.ExecuteScalar()}");
            
            command.CommandText = "SELECT ItemId, VIN, Rack, NoRack, StatusItem, IsActive FROM Items WHERE VIN IN ('TA0180', 'HN048A', 'HN0390')";
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    Console.WriteLine($"ID: {reader[0]}, VIN: {reader[1]}, Rack: {reader[2]}, NoRack: {reader[3]}, Status: {reader[4]}, IsActive: {reader[5]}");
                }
            }
        }
    }
}
