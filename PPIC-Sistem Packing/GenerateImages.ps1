Add-Type -AssemblyName System.Drawing

$spisDest = "c:\Sistem Packing\SistemPacking.Web\wwwroot\images\std-packing\spis"
$sppsDest = "c:\Sistem Packing\SistemPacking.Web\wwwroot\images\std-packing\spps"

New-Item -ItemType Directory -Force -Path $spisDest
New-Item -ItemType Directory -Force -Path $sppsDest

$colors = @("Red", "Blue", "Green", "Purple", "Orange", "Teal", "Navy", "Maroon", "Olive", "DimGray", "DarkCyan", "DarkGoldenrod", "DarkMagenta", "DarkRed", "DarkSlateBlue", "DarkGreen", "DarkKhaki", "DarkOliveGreen", "DarkSalmon", "DarkSeaGreen")

for ($i = 1; $i -le 20; $i++) {
    $code = "SP-{0:D4}" -f $i
    $colorName = $colors[$i - 1]
    $color = [System.Drawing.Color]::FromName($colorName)
    
    $bmpSpis = New-Object System.Drawing.Bitmap(400, 400)
    $gSpis = [System.Drawing.Graphics]::FromImage($bmpSpis)
    $gSpis.Clear([System.Drawing.Color]::White)
    $brush = New-Object System.Drawing.SolidBrush($color)
    $gSpis.FillRectangle($brush, 50, 50, 300, 300)
    
    $font = New-Object System.Drawing.Font("Arial", 24, [System.Drawing.FontStyle]::Bold)
    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $gSpis.DrawString("SPIS", $font, $textBrush, 150, 150)
    $gSpis.DrawString($code, $font, $textBrush, 120, 200)
    
    $bmpSpis.Save("$spisDest\$code.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $gSpis.Dispose()
    $bmpSpis.Dispose()
    
    $bmpSpps = New-Object System.Drawing.Bitmap(400, 400)
    $gSpps = [System.Drawing.Graphics]::FromImage($bmpSpps)
    $gSpps.Clear([System.Drawing.Color]::White)
    $brushSpps = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::LightGray)
    $gSpps.FillRectangle($brushSpps, 20, 20, 360, 360)
    
    $pen = New-Object System.Drawing.Pen($color, 10)
    $gSpps.DrawRectangle($pen, 40, 40, 320, 320)
    
    $textBrushDark = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::Black)
    $gSpps.DrawString("SPPS", $font, $textBrushDark, 150, 150)
    $gSpps.DrawString($code, $font, $textBrushDark, 120, 200)
    
    $bmpSpps.Save("$sppsDest\$code.png", [System.Drawing.Imaging.ImageFormat]::Png)
    $gSpps.Dispose()
    $bmpSpps.Dispose()
}
