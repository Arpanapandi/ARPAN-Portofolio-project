# Script PowerShell untuk Deployment Database ke Production
# Usage: .\Deploy-ToProduction.ps1 -ServerName "localhost" -DatabaseName "PPIC_DeliveryControl" -UseWindowsAuth

param(
    [Parameter(Mandatory=$true)]
    [string]$ServerName,
    
    [Parameter(Mandatory=$false)]
    [string]$DatabaseName = "PPIC_DeliveryControl",
    
    [Parameter(Mandatory=$false)]
    [string]$Username,
    
    [Parameter(Mandatory=$false)]
    [string]$Password,
    
    [Parameter(Mandatory=$false)]
    [switch]$UseWindowsAuth,
    
    [Parameter(Mandatory=$false)]
    [string]$MigrationScriptPath = "Migrations\Production_Migration.sql",
    
    [Parameter(Mandatory=$false)]
    [switch]$CreateDatabase,
    
    [Parameter(Mandatory=$false)]
    [switch]$BackupFirst
)

# Warna output
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

Write-ColorOutput Green "=========================================="
Write-ColorOutput Green "  Delivery Control - Production Deployment"
Write-ColorOutput Green "=========================================="
Write-Output ""

# Validasi parameter
if (-not $UseWindowsAuth -and (-not $Username -or -not $Password)) {
    Write-ColorOutput Red "ERROR: Username dan Password harus disediakan jika tidak menggunakan Windows Authentication"
    exit 1
}

# Cek apakah file migration script ada
if (-not (Test-Path $MigrationScriptPath)) {
    Write-ColorOutput Red "ERROR: File migration script tidak ditemukan: $MigrationScriptPath"
    Write-Output "Pastikan file Production_Migration.sql ada di folder Migrations/"
    exit 1
}

Write-ColorOutput Yellow "Konfigurasi Deployment:"
Write-Output "  Server: $ServerName"
Write-Output "  Database: $DatabaseName"
Write-Output "  Authentication: $(if ($UseWindowsAuth) { 'Windows Authentication' } else { 'SQL Authentication' })"
Write-Output "  Migration Script: $MigrationScriptPath"
Write-Output ""

# Build connection string untuk sqlcmd
if ($UseWindowsAuth) {
    $sqlcmdArgs = "-S", $ServerName, "-E"
} else {
    $sqlcmdArgs = "-S", $ServerName, "-U", $Username, "-P", $Password
}

# Cek koneksi ke SQL Server
Write-ColorOutput Yellow "Mengecek koneksi ke SQL Server..."
try {
    $testQuery = "SELECT @@VERSION"
    if ($UseWindowsAuth) {
        $result = sqlcmd -S $ServerName -E -Q $testQuery -h -1 -W 2>&1
    } else {
        $result = sqlcmd -S $ServerName -U $Username -P $Password -Q $testQuery -h -1 -W 2>&1
    }
    
    if ($LASTEXITCODE -ne 0) {
        throw "Gagal connect ke SQL Server"
    }
    Write-ColorOutput Green "✓ Koneksi ke SQL Server berhasil"
} catch {
    Write-ColorOutput Red "ERROR: Gagal connect ke SQL Server: $_"
    exit 1
}

# Backup database jika diminta dan database sudah ada
if ($BackupFirst) {
    Write-ColorOutput Yellow "Mengecek apakah database sudah ada..."
    $checkDbQuery = "SELECT COUNT(*) FROM sys.databases WHERE name = '$DatabaseName'"
    if ($UseWindowsAuth) {
        $dbExists = sqlcmd -S $ServerName -E -Q $checkDbQuery -h -1 -W 2>&1
    } else {
        $dbExists = sqlcmd -S $ServerName -U $Username -P $Password -Q $checkDbQuery -h -1 -W 2>&1
    }
    
    if ($dbExists -match "1") {
        Write-ColorOutput Yellow "Database sudah ada. Membuat backup..."
        $backupPath = "C:\Backup\${DatabaseName}_BeforeMigration_$(Get-Date -Format 'yyyyMMdd_HHmmss').bak"
        
        # Pastikan folder backup ada
        $backupDir = Split-Path $backupPath
        if (-not (Test-Path $backupDir)) {
            New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
        }
        
        $backupQuery = "BACKUP DATABASE [$DatabaseName] TO DISK = '$backupPath' WITH FORMAT, COMPRESSION"
        if ($UseWindowsAuth) {
            sqlcmd -S $ServerName -E -Q $backupQuery 2>&1 | Out-Null
        } else {
            sqlcmd -S $ServerName -U $Username -P $Password -Q $backupQuery 2>&1 | Out-Null
        }
        
        if ($LASTEXITCODE -eq 0) {
            Write-ColorOutput Green "✓ Backup berhasil: $backupPath"
        } else {
            Write-ColorOutput Yellow "WARNING: Backup mungkin gagal, lanjutkan? (Y/N)"
            $response = Read-Host
            if ($response -ne "Y" -and $response -ne "y") {
                exit 0
            }
        }
    }
}

# Buat database jika diminta
if ($CreateDatabase) {
    Write-ColorOutput Yellow "Membuat database jika belum ada..."
    $createDbQuery = @"
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = '$DatabaseName')
BEGIN
    CREATE DATABASE [$DatabaseName];
    PRINT 'Database $DatabaseName berhasil dibuat.';
END
ELSE
BEGIN
    PRINT 'Database $DatabaseName sudah ada.';
END
"@
    
    $createDbQuery | Out-File -FilePath "$env:TEMP\create_db.sql" -Encoding UTF8
    
    if ($UseWindowsAuth) {
        sqlcmd -S $ServerName -E -i "$env:TEMP\create_db.sql" 2>&1 | Out-Null
    } else {
        sqlcmd -S $ServerName -U $Username -P $Password -i "$env:TEMP\create_db.sql" 2>&1 | Out-Null
    }
    
    if ($LASTEXITCODE -eq 0) {
        Write-ColorOutput Green "✓ Database siap"
    } else {
        Write-ColorOutput Red "ERROR: Gagal membuat database"
        exit 1
    }
    
    Remove-Item "$env:TEMP\create_db.sql" -ErrorAction SilentlyContinue
}

# Jalankan migration script
Write-ColorOutput Yellow "Menjalankan script migrasi..."
Write-Output "Ini mungkin memakan waktu beberapa menit..."

$startTime = Get-Date

if ($UseWindowsAuth) {
    $output = sqlcmd -S $ServerName -E -d $DatabaseName -i $MigrationScriptPath -b 2>&1
} else {
    $output = sqlcmd -S $ServerName -U $Username -P $Password -d $DatabaseName -i $MigrationScriptPath -b 2>&1
}

$endTime = Get-Date
$duration = $endTime - $startTime

if ($LASTEXITCODE -eq 0) {
    Write-ColorOutput Green "✓ Migrasi berhasil!"
    Write-Output "Waktu eksekusi: $($duration.TotalSeconds) detik"
} else {
    Write-ColorOutput Red "ERROR: Migrasi gagal!"
    Write-Output "Output error:"
    Write-Output $output
    exit 1
}

# Verifikasi migrasi
Write-ColorOutput Yellow "Memverifikasi migrasi..."
$verifyQuery = "SELECT COUNT(*) as MigrationCount FROM __EFMigrationsHistory"
if ($UseWindowsAuth) {
    $migrationCount = sqlcmd -S $ServerName -E -d $DatabaseName -Q $verifyQuery -h -1 -W 2>&1
} else {
    $migrationCount = sqlcmd -S $ServerName -U $Username -P $Password -d $DatabaseName -Q $verifyQuery -h -1 -W 2>&1
}

if ($migrationCount -match "^\d+$") {
    Write-ColorOutput Green "✓ Jumlah migrasi yang ter-apply: $migrationCount"
} else {
    Write-ColorOutput Yellow "WARNING: Tidak bisa memverifikasi jumlah migrasi"
}

# Cek tabel yang dibuat
Write-ColorOutput Yellow "Memverifikasi tabel yang dibuat..."
$tableQuery = "SELECT COUNT(*) as TableCount FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE'"
if ($UseWindowsAuth) {
    $tableCount = sqlcmd -S $ServerName -E -d $DatabaseName -Q $tableQuery -h -1 -W 2>&1
} else {
    $tableCount = sqlcmd -S $ServerName -U $Username -P $Password -d $DatabaseName -Q $tableQuery -h -1 -W 2>&1
}

if ($tableCount -match "^\d+$") {
    Write-ColorOutput Green "✓ Jumlah tabel: $tableCount"
} else {
    Write-ColorOutput Yellow "WARNING: Tidak bisa memverifikasi jumlah tabel"
}

# Cek user default
Write-ColorOutput Yellow "Memverifikasi user default..."
$userQuery = "SELECT COUNT(*) as UserCount FROM Users"
if ($UseWindowsAuth) {
    $userCount = sqlcmd -S $ServerName -E -d $DatabaseName -Q $userQuery -h -1 -W 2>&1
} else {
    $userCount = sqlcmd -S $ServerName -U $Username -P $Password -d $DatabaseName -Q $userQuery -h -1 -W 2>&1
}

if ($userCount -match "^\d+$") {
    if ([int]$userCount -gt 0) {
        Write-ColorOutput Green "✓ User default sudah dibuat: $userCount user"
    } else {
        Write-ColorOutput Yellow "WARNING: Belum ada user di database. Aplikasi akan membuat user default saat pertama kali dijalankan."
    }
}

Write-Output ""
Write-ColorOutput Green "=========================================="
Write-ColorOutput Green "  Deployment Selesai!"
Write-ColorOutput Green "=========================================="
Write-Output ""
Write-ColorOutput Yellow "Langkah selanjutnya:"
Write-Output "1. Update appsettings.Production.json dengan connection string yang benar"
Write-Output "2. Set environment variable ASPNETCORE_ENVIRONMENT=Production"
Write-Output "3. Jalankan aplikasi dan test login"
Write-Output "4. Ganti password default user admin dan user"
Write-Output ""
Write-ColorOutput Yellow "User default:"
Write-Output "  - Username: admin, Password: admin123, Role: Admin"
Write-Output "  - Username: user, Password: user123, Role: User"
Write-Output ""

