# Quick Check: Validasi Backend Only
# Cara pakai: powershell -ExecutionPolicy Bypass -File .\check-backend.ps1

$staged = git diff --cached --name-only
$modified = git diff --name-only
$all = ($staged + $modified) | Select-Object -Unique

if ($all.Count -eq 0) {
    Write-Host "✓ Tidak ada perubahan" -ForegroundColor Green
    exit 0
}

$frontend = $all | Where-Object { $_ -like "Views/*" -or $_ -like "wwwroot/*" }

if ($frontend) {
    Write-Host "❌ ERROR: Ada perubahan di FRONTEND!" -ForegroundColor Red
    $frontend | ForEach-Object { Write-Host "   - $_" -ForegroundColor Red }
    Write-Host ""
    Write-Host "Jalankan: git restore --staged Views wwwroot" -ForegroundColor Yellow
    Write-Host "Jalankan: git restore Views wwwroot" -ForegroundColor Yellow
    exit 1
} else {
    Write-Host "✓ Tidak ada perubahan di frontend" -ForegroundColor Green
    Write-Host ""
    Write-Host "File yang berubah:" -ForegroundColor Cyan
    $all | ForEach-Object { Write-Host "   - $_" -ForegroundColor Gray }
    exit 0
}

