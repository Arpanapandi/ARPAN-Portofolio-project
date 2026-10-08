$urls = @(
    'http://localhost:5206/',
    'http://localhost:5206/PreparationSchedule',
    'http://localhost:5206/DeliverySchedules',
    'http://localhost:5206/Home/Index',
    'http://localhost:5206/PreparationSchedule?filterDate=2026-02-23',
    'http://localhost:5206/DeliverySchedules?startDate=2026-02-23',
    'http://localhost:5206/',
    'http://localhost:5206/PreparationSchedule',
    'http://localhost:5206/DeliverySchedules',
    'http://localhost:5206/'
)

Write-Host "Starting 10 concurrent requests..."
$start = Get-Date

$jobs = foreach ($url in $urls) {
    Start-Job -ScriptBlock {
        param($u)
        try {
            $r = Invoke-WebRequest -Uri $u -TimeoutSec 10 -UseBasicParsing -ErrorAction Stop
            [PSCustomObject]@{ Url=$u.Substring($u.Length - [Math]::Min(40,$u.Length)); Status=$r.StatusCode; OK=$true }
        } catch {
            [PSCustomObject]@{ Url=$u.Substring($u.Length - [Math]::Min(40,$u.Length)); Status=$_.Exception.Message.Substring(0,[Math]::Min(60,$_.Exception.Message.Length)); OK=$false }
        }
    } -ArgumentList $url
}

$results = $jobs | Wait-Job | Receive-Job
$elapsed = ((Get-Date) - $start).TotalSeconds

$results | Format-Table -AutoSize
$ok = ($results | Where-Object { $_.OK }).Count
$fail = ($results | Where-Object { -not $_.OK }).Count
Write-Host "=== RESULT: $ok OK, $fail FAILED in $([Math]::Round($elapsed,2))s ==="
