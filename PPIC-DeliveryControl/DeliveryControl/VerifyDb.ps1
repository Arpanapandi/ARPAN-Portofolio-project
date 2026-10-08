
$connectionString = "Server=10.14.149.34;Database=PPIC_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true;MultipleActiveResultSets=true"

try {
    $conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $conn.Open()
    Write-Host "Connection Successful!" -ForegroundColor Green

    $cmd = $conn.CreateCommand()
    $cmd.CommandText = "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_NAME"
    
    $reader = $cmd.ExecuteReader()
    Write-Host "`n📊 Existing Tables:" -ForegroundColor Cyan
    while ($reader.Read()) {
        Write-Host " - $($reader["TABLE_NAME"])"
    }
    $reader.Close()

    # Check for Migration History specifically
    $cmd.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory"
    $reader = $cmd.ExecuteReader()
    Write-Host "`n📜 Applied Migrations:" -ForegroundColor Cyan
    while ($reader.Read()) {
        Write-Host " - $($reader["MigrationId"])"
    }
    $conn.Close()
}
catch {
    Write-Error "❌ Connection Failed: $_"
}
