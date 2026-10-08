# Script untuk mengecek data ImageUrl di database
# Script ini akan menampilkan data ImageUrl dari semua machines di database

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "CEK DATA IMAGEURL DI DATABASE" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# URL endpoint untuk cek data ImageUrl
$url = "http://localhost:6001/Admin/CheckMachineImages"

Write-Host "Mengambil data dari: $url" -ForegroundColor Yellow
Write-Host ""

try {
    # Request ke endpoint
    $response = Invoke-RestMethod -Uri $url -Method Get -ContentType "application/json"
    
    Write-Host "=== SUMMARY ===" -ForegroundColor Green
    Write-Host "Total Machines: $($response.TotalMachines)" -ForegroundColor White
    Write-Host "Machines with ImageUrl: $($response.MachinesWithImage)" -ForegroundColor Green
    Write-Host "Machines without ImageUrl: $($response.MachinesWithoutImage)" -ForegroundColor Red
    Write-Host ""
    
    Write-Host "=== DETAIL MACHINES ===" -ForegroundColor Green
    Write-Host ""
    
    foreach ($machine in $response.Machines) {
        $status = if ($machine.HasImageUrl) { "✓ HAS IMAGE" } else { "✗ NO IMAGE" }
        $color = if ($machine.HasImageUrl) { "Green" } else { "Red" }
        
        Write-Host "Machine ID: $($machine.Id)" -ForegroundColor White
        Write-Host "  Name: $($machine.Name)" -ForegroundColor Gray
        Write-Host "  Status: $status" -ForegroundColor $color
        Write-Host "  ImageUrl: $($machine.ImageUrl)" -ForegroundColor Gray
        Write-Host "  ImageUrl Length: $($machine.ImageUrlLength)" -ForegroundColor Gray
        Write-Host ""
    }
    
    Write-Host "========================================" -ForegroundColor Cyan
    Write-Host "Selesai!" -ForegroundColor Green
}
catch {
    Write-Host "ERROR: Tidak dapat mengambil data dari database" -ForegroundColor Red
    Write-Host "Pastikan aplikasi sedang berjalan di http://localhost:6001" -ForegroundColor Yellow
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
}

