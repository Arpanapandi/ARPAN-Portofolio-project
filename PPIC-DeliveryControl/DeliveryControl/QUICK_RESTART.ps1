# ========================================
# QUICK RESTART (Tanpa Clean Build)
# ========================================

Write-Host "⚡ QUICK RESTART APLIKASI..." -ForegroundColor Cyan

# Stop aplikasi
$ports = @(5206, 7081)
$processIds = @()

foreach ($port in $ports) {
    $connections = netstat -ano | Select-String ":$port" | Select-String "LISTENING"
    foreach ($conn in $connections) {
        $parts = $conn -split '\s+' | Where-Object { $_ -ne '' }
        $pid = $parts[-1]
        if ($pid -and $pid -ne '0' -and $processIds -notcontains $pid) {
            $processIds += $pid
        }
    }
}

if ($processIds.Count -gt 0) {
    foreach ($pid in $processIds) {
        Stop-Process -Id $pid -Force -ErrorAction SilentlyContinue
    }
    Write-Host "✅ Aplikasi dihentikan" -ForegroundColor Green
    Start-Sleep -Seconds 1
}

# Build & Run
Write-Host "🔨 Building..." -ForegroundColor Yellow
dotnet build --no-restore > $null 2>&1

Write-Host "🚀 Starting..." -ForegroundColor Yellow
Write-Host ""
Write-Host "📍 http://localhost:5206" -ForegroundColor Green
Write-Host "⚠️  Jangan lupa: Ctrl + Shift + R di browser!" -ForegroundColor Yellow
Write-Host ""

dotnet run

