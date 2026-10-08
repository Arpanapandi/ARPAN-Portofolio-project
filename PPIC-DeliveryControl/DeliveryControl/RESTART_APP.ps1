# ========================================
# SCRIPT RESTART APLIKASI DELIVERY CONTROL
# ========================================

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "   RESTART APLIKASI DELIVERY CONTROL   " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Stop aplikasi yang sedang jalan
Write-Host "⏹️  Step 1: Menghentikan aplikasi yang sedang jalan..." -ForegroundColor Yellow

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
        try {
            $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
            if ($process) {
                Write-Host "   Menghentikan process: $($process.ProcessName) (PID: $pid)" -ForegroundColor Yellow
                Stop-Process -Id $pid -Force
                Write-Host "   ✅ Process berhasil dihentikan!" -ForegroundColor Green
            }
        }
        catch {
            Write-Host "   ⚠️  Tidak dapat menghentikan process $pid" -ForegroundColor Red
        }
    }
    Start-Sleep -Seconds 2
} else {
    Write-Host "   ℹ️  Tidak ada aplikasi yang sedang jalan" -ForegroundColor Gray
}

Write-Host ""

# Step 2: Clean build artifacts
Write-Host "🧹 Step 2: Membersihkan file build lama..." -ForegroundColor Yellow

if (Test-Path "bin") {
    Remove-Item -Recurse -Force bin
    Write-Host "   ✅ Folder 'bin' dihapus" -ForegroundColor Green
}

if (Test-Path "obj") {
    Remove-Item -Recurse -Force obj
    Write-Host "   ✅ Folder 'obj' dihapus" -ForegroundColor Green
}

Write-Host ""

# Step 3: Build aplikasi
Write-Host "🔨 Step 3: Building aplikasi..." -ForegroundColor Yellow
Write-Host ""

dotnet build

if ($LASTEXITCODE -ne 0) {
    Write-Host ""
    Write-Host "❌ BUILD GAGAL! Ada error di kode." -ForegroundColor Red
    Write-Host "   Silakan perbaiki error di atas terlebih dahulu." -ForegroundColor Red
    Write-Host ""
    exit 1
}

Write-Host ""
Write-Host "✅ Build berhasil!" -ForegroundColor Green
Write-Host ""

# Step 4: Run aplikasi
Write-Host "🚀 Step 4: Menjalankan aplikasi..." -ForegroundColor Yellow
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Aplikasi akan jalan di:" -ForegroundColor White
Write-Host "  HTTP  : http://localhost:5206" -ForegroundColor Green
Write-Host "  HTTPS : https://localhost:7081" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "⚠️  Setelah aplikasi jalan, WAJIB:" -ForegroundColor Yellow
Write-Host "   1. Buka browser" -ForegroundColor White
Write-Host "   2. Tekan Ctrl + Shift + R (Hard Refresh)" -ForegroundColor White
Write-Host ""
Write-Host "📌 Untuk stop aplikasi: Tekan Ctrl + C" -ForegroundColor Yellow
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Tunggu sebentar sebelum run
Start-Sleep -Seconds 2

dotnet run --launch-profile production

