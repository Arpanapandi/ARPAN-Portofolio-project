# Script untuk menjalankan Migration AddManPowerFeature
# Pastikan SQL Server berjalan dan connection string benar

$connectionString = "Server=10.14.149.34;Database=OeeSystemDb;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=True;Connection Timeout=60;Command Timeout=120;Encrypt=True;MultipleActiveResultSets=true"

$sqlScript = Get-Content -Path "Migrations\AddManPowerFeature.sql" -Raw

Write-Host "Menjalankan Migration AddManPowerFeature..." -ForegroundColor Yellow

try {
    # Install SqlServer module jika belum ada
    if (-not (Get-Module -ListAvailable -Name SqlServer)) {
        Write-Host "Menginstall module SqlServer..." -ForegroundColor Yellow
        Install-Module -Name SqlServer -Force -Scope CurrentUser -AllowClobber
    }
    
    Import-Module SqlServer
    
    # Ekstrak connection details dari connection string
    $server = "10.14.149.34"
    $database = "OeeSystemDb"
    $username = "usrvelasto"
    $password = "H1s@na2025!!"
    
    # Jalankan SQL script
    Invoke-Sqlcmd -ServerInstance $server -Database $database -Username $username -Password $password -Query $sqlScript -TrustServerCertificate
    
    Write-Host "Migration berhasil dijalankan!" -ForegroundColor Green
}
catch {
    Write-Host "Error menjalankan migration: $_" -ForegroundColor Red
    Write-Host "Pastikan:" -ForegroundColor Yellow
    Write-Host "1. SQL Server berjalan" -ForegroundColor Yellow
    Write-Host "2. Connection string benar" -ForegroundColor Yellow
    Write-Host "3. User memiliki permission untuk membuat tabel" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Atau jalankan script SQL secara manual di SQL Server Management Studio" -ForegroundColor Cyan
}

