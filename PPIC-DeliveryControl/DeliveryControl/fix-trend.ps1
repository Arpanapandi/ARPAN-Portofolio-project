$path = "c:\DeliveryControl_backup_old\DeliveryControl\Views\Stock\TrendCriticalStock.cshtml"
$content = Get-Content -Raw -Path $path

$content = $content.Replace("1.5D", "2D")
$content = $content.Replace("1_5D", "2D")
$content = $content.Replace("< 1.5 Days", "< 2 Days")

$content = $content.Replace("1D", "1.5D")
$content = $content.Replace("1_D", "1_5D") # Just in case
$content = $content.Replace("btnLv1D", "btnLv1_5D")
$content = $content.Replace("Below1D", "Below1_5D")
$content = $content.Replace("< 1 Day", "< 1.5 Days")

Set-Content -Path $path -Value $content
