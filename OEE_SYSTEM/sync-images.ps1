# Script untuk sync images antara Frontend dan Backend
# Usage: .\sync-images.ps1 -FrontendPath "C:\path\to\frontend\wwwroot\images\machines" -Bidirectional

param(
    [string]$FrontendPath = "",
    [string]$BackendPath = "C:\OEE SYSTEM\OEE_SYSTEM\wwwroot\images\machines",
    [switch]$Bidirectional = $false
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SYNC MACHINE IMAGES" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Validasi path
if (-not (Test-Path $BackendPath)) {
    Write-Host "❌ ERROR: Backend path tidak ditemukan: $BackendPath" -ForegroundColor Red
    exit 1
}

# Sync dari Frontend ke Backend
if (-not [string]::IsNullOrWhiteSpace($FrontendPath) -and (Test-Path $FrontendPath)) {
    Write-Host "📥 Syncing from Frontend to Backend..." -ForegroundColor Yellow
    Write-Host "   From: $FrontendPath" -ForegroundColor Gray
    Write-Host "   To: $BackendPath" -ForegroundColor Gray
    Write-Host ""
    
    $frontendFiles = Get-ChildItem -Path $FrontendPath -File -ErrorAction SilentlyContinue
    $copiedCount = 0
    
    foreach ($file in $frontendFiles) {
        $destPath = Join-Path $BackendPath $file.Name
        try {
            Copy-Item -Path $file.FullName -Destination $destPath -Force -ErrorAction Stop
            Write-Host "  ✓ Copied: $($file.Name)" -ForegroundColor Green
            $copiedCount++
        }
        catch {
            Write-Host "  ✗ Failed: $($file.Name) - $($_.Exception.Message)" -ForegroundColor Red
        }
    }
    
    Write-Host ""
    Write-Host "✅ Copied $copiedCount file(s) from Frontend to Backend" -ForegroundColor Green
    Write-Host ""
}
elseif (-not [string]::IsNullOrWhiteSpace($FrontendPath)) {
    Write-Host "⚠️  WARNING: Frontend path tidak ditemukan: $FrontendPath" -ForegroundColor Yellow
    Write-Host "   Skipping Frontend to Backend sync..." -ForegroundColor Yellow
    Write-Host ""
}

# Sync dari Backend ke Frontend (jika bidirectional)
if ($Bidirectional -and -not [string]::IsNullOrWhiteSpace($FrontendPath)) {
    if (Test-Path $FrontendPath) {
        Write-Host "📤 Syncing from Backend to Frontend..." -ForegroundColor Yellow
        Write-Host "   From: $BackendPath" -ForegroundColor Gray
        Write-Host "   To: $FrontendPath" -ForegroundColor Gray
        Write-Host ""
        
        $backendFiles = Get-ChildItem -Path $BackendPath -File -ErrorAction SilentlyContinue
        $copiedCount = 0
        
        foreach ($file in $backendFiles) {
            $destPath = Join-Path $FrontendPath $file.Name
            try {
                Copy-Item -Path $file.FullName -Destination $destPath -Force -ErrorAction Stop
                Write-Host "  ✓ Copied: $($file.Name)" -ForegroundColor Green
                $copiedCount++
            }
            catch {
                Write-Host "  ✗ Failed: $($file.Name) - $($_.Exception.Message)" -ForegroundColor Red
            }
        }
        
        Write-Host ""
        Write-Host "✅ Copied $copiedCount file(s) from Backend to Frontend" -ForegroundColor Green
        Write-Host ""
    }
    else {
        Write-Host "⚠️  WARNING: Frontend path tidak ditemukan: $FrontendPath" -ForegroundColor Yellow
        Write-Host "   Skipping Backend to Frontend sync..." -ForegroundColor Yellow
        Write-Host ""
    }
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SUMMARY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$backendFileCount = (Get-ChildItem -Path $BackendPath -File -ErrorAction SilentlyContinue).Count
Write-Host "Backend images: $backendFileCount file(s)" -ForegroundColor White

if (-not [string]::IsNullOrWhiteSpace($FrontendPath) -and (Test-Path $FrontendPath)) {
    $frontendFileCount = (Get-ChildItem -Path $FrontendPath -File -ErrorAction SilentlyContinue).Count
    Write-Host "Frontend images: $frontendFileCount file(s)" -ForegroundColor White
}

Write-Host ""
Write-Host "✨ Sync completed!" -ForegroundColor Green

