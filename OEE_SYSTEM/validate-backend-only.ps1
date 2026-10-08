# Script Validasi: Pastikan Hanya Backend yang Diubah
# Gunakan script ini sebelum commit untuk memastikan tidak ada perubahan frontend
#
# CARA MENJALANKAN:
#   powershell -ExecutionPolicy Bypass -File .\validate-backend-only.ps1
#   ATAU
#   Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
#   .\validate-backend-only.ps1

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "VALIDASI PERUBAHAN BACKEND ONLY" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Daftar direktori/file yang termasuk BACKEND (boleh diubah)
$backendPaths = @(
    "Controllers",
    "Models",
    "Data",
    "Services",
    "Hubs",
    "Program.cs",
    "appsettings.json",
    "OeeSystem.csproj",
    "*.sql"
)

# Daftar direktori/file yang termasuk FRONTEND (TIDAK BOLEH diubah)
$frontendPaths = @(
    "Views",
    "wwwroot"
)

# Cek perubahan yang sudah di-stage
Write-Host "1. Mengecek file yang sudah di-stage (git add)..." -ForegroundColor Yellow
$stagedFiles = git diff --cached --name-only

if ($stagedFiles.Count -eq 0) {
    Write-Host "   Tidak ada file yang di-stage." -ForegroundColor Gray
} else {
    Write-Host "   Ditemukan $($stagedFiles.Count) file yang di-stage:" -ForegroundColor Gray
    foreach ($file in $stagedFiles) {
        Write-Host "   - $file" -ForegroundColor Gray
    }
}

# Cek perubahan yang belum di-stage
Write-Host ""
Write-Host "2. Mengecek file yang belum di-stage (modified)..." -ForegroundColor Yellow
$modifiedFiles = git diff --name-only

if ($modifiedFiles.Count -eq 0) {
    Write-Host "   Tidak ada file yang dimodifikasi." -ForegroundColor Gray
} else {
    Write-Host "   Ditemukan $($modifiedFiles.Count) file yang dimodifikasi:" -ForegroundColor Gray
    foreach ($file in $modifiedFiles) {
        Write-Host "   - $file" -ForegroundColor Gray
    }
}

# Gabungkan semua file yang berubah
$allChangedFiles = @()
if ($stagedFiles) { $allChangedFiles += $stagedFiles }
if ($modifiedFiles) { $allChangedFiles += $modifiedFiles }
$allChangedFiles = $allChangedFiles | Select-Object -Unique

if ($allChangedFiles.Count -eq 0) {
    Write-Host ""
    Write-Host "Tidak ada perubahan yang terdeteksi." -ForegroundColor Green
    exit 0
}

# Validasi: Cek apakah ada perubahan di frontend
Write-Host ""
Write-Host "3. Validasi: Mencari perubahan di FRONTEND..." -ForegroundColor Yellow
$frontendChanges = @()

foreach ($file in $allChangedFiles) {
    foreach ($frontendPath in $frontendPaths) {
        if ($file -like "$frontendPath/*" -or $file -eq $frontendPath) {
            $frontendChanges += $file
        }
    }
}

if ($frontendChanges.Count -gt 0) {
    Write-Host ""
    Write-Host "❌ ERROR: Ditemukan perubahan di FRONTEND!" -ForegroundColor Red
    Write-Host ""
    Write-Host "File berikut termasuk FRONTEND dan TIDAK BOLEH diubah:" -ForegroundColor Red
    foreach ($file in $frontendChanges) {
        Write-Host "   - $file" -ForegroundColor Red
    }
    Write-Host ""
    Write-Host "SOLUSI:" -ForegroundColor Yellow
    Write-Host "   1. Unstage file frontend: git restore --staged Views wwwroot" -ForegroundColor Yellow
    Write-Host "   2. Restore perubahan frontend: git restore Views wwwroot" -ForegroundColor Yellow
    Write-Host "   3. Pastikan hanya file backend yang di-stage" -ForegroundColor Yellow
    Write-Host ""
    exit 1
} else {
    Write-Host "   ✓ Tidak ada perubahan di frontend" -ForegroundColor Green
}

# Validasi: Cek apakah ada perubahan di backend
Write-Host ""
Write-Host "4. Validasi: Mencari perubahan di BACKEND..." -ForegroundColor Yellow
$backendChanges = @()

foreach ($file in $allChangedFiles) {
    $isBackend = $false
    foreach ($backendPath in $backendPaths) {
        if ($file -like "$backendPath/*" -or $file -like $backendPath -or $file -eq $backendPath) {
            $isBackend = $true
            break
        }
    }
    if ($isBackend) {
        $backendChanges += $file
    }
}

if ($backendChanges.Count -gt 0) {
    Write-Host "   ✓ Ditemukan $($backendChanges.Count) perubahan di backend:" -ForegroundColor Green
    foreach ($file in $backendChanges) {
        Write-Host "     - $file" -ForegroundColor Green
    }
} else {
    Write-Host "   ⚠ Tidak ada perubahan di backend yang terdeteksi" -ForegroundColor Yellow
}

# Cek file yang tidak termasuk backend atau frontend
Write-Host ""
Write-Host "5. File lain yang berubah (bukan backend/frontend):" -ForegroundColor Yellow
$otherFiles = @()
foreach ($file in $allChangedFiles) {
    $isBackend = $false
    $isFrontend = $false
    
    foreach ($backendPath in $backendPaths) {
        if ($file -like "$backendPath/*" -or $file -like $backendPath -or $file -eq $backendPath) {
            $isBackend = $true
            break
        }
    }
    
    foreach ($frontendPath in $frontendPaths) {
        if ($file -like "$frontendPath/*" -or $file -eq $frontendPath) {
            $isFrontend = $true
            break
        }
    }
    
    if (-not $isBackend -and -not $isFrontend) {
        $otherFiles += $file
    }
}

if ($otherFiles.Count -gt 0) {
    Write-Host "   File berikut berubah (review manual):" -ForegroundColor Yellow
    foreach ($file in $otherFiles) {
        Write-Host "     - $file" -ForegroundColor Yellow
    }
}

# Kesimpulan
Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
if ($frontendChanges.Count -eq 0) {
    Write-Host "✓ VALIDASI BERHASIL: Hanya backend yang berubah" -ForegroundColor Green
    Write-Host ""
    Write-Host "Anda bisa commit dengan aman:" -ForegroundColor Green
    Write-Host "  git commit -m 'Backend updates: [deskripsi perubahan]'" -ForegroundColor Cyan
    exit 0
} else {
    Write-Host "❌ VALIDASI GAGAL: Ada perubahan di frontend!" -ForegroundColor Red
    Write-Host ""
    exit 1
}

