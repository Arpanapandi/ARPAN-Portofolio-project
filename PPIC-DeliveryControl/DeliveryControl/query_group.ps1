
$connStr = 'Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true;MultipleActiveResultSets=true'
$sql = "
SELECT ScheduleNumber, Status, Area, Route, Cycle
FROM DeliverySchedules 
WHERE CustomerId = 1 
AND ScheduledDate >= '2026-06-08' 
AND ScheduledDate < '2026-06-09'
AND Cycle = 'C2'
"
Invoke-Sqlcmd -ConnectionString $connStr -Query $sql | Format-Table -AutoSize

