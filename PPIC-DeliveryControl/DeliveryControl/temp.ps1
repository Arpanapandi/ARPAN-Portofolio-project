Add-Type -Path 'C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App\8.0.0\Microsoft.Data.Sqlite.dll'
$db = 'D:\DeliveryControl_backup_old\DeliveryControl\delivery.db'
$conn = New-Object Microsoft.Data.Sqlite.SqliteConnection("Data Source=$db")
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = 'SELECT ItemCode, COUNT(Id) FROM Items WHERE IsActive=1 AND IsDeleted=0 GROUP BY ItemCode HAVING COUNT(Id) > 1'
$reader = $cmd.ExecuteReader()
while ($reader.Read()) { Write-Host ($reader.GetString(0) + ': ' + $reader.GetInt32(1)) }
$conn.Close()
