# Script PowerShell untuk menambahkan kolom ActualEnterDockTime
# Pastikan aplikasi sudah di-stop sebelum menjalankan script ini

$dbName = "PPIC_DeliveryControl"
$serverName = "(localdb)\mssqllocaldb"

# SQL Command
$sqlCommand = @"
IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[dbo].[DeliverySchedules]') 
    AND name = 'ActualEnterDockTime'
)
BEGIN
    ALTER TABLE [DeliverySchedules] 
    ADD [ActualEnterDockTime] datetime2 NULL;
    PRINT 'Kolom ActualEnterDockTime berhasil ditambahkan';
END
ELSE
BEGIN
    PRINT 'Kolom ActualEnterDockTime sudah ada';
END
"@

Write-Host "Menambahkan kolom ActualEnterDockTime ke database..."
Write-Host "ConnectionString: Server=$serverName;Database=$dbName;Trusted_Connection=True;"
Write-Host "Server: $serverName"
Write-Host "Database: $dbName"
Write-Host ""

try {
    # Try using sqlcmd
    $sqlcmdPath = "sqlcmd"
    $sqlFile = [System.IO.Path]::GetTempFileName() + ".sql"
    $sqlCommand | Out-File -FilePath $sqlFile -Encoding UTF8
    
    $arguments = "-S", $serverName, "-d", $dbName, "-i", $sqlFile, "-E"
    
    Write-Host "Menjalankan SQL command..."
    & $sqlcmdPath $arguments
    
    Remove-Item $sqlFile -ErrorAction SilentlyContinue
    
    Write-Host ""
    Write-Host "Selesai! Silakan restart aplikasi."
}
catch {
    Write-Host "Error: $_"
    Write-Host ""
    Write-Host "Alternatif: Jalankan SQL berikut secara manual di SQL Server Management Studio:"
    Write-Host $sqlCommand
}

