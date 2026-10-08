@echo off
:: Berpindah ke directory tempat file .bat ini berada
cd /d "%~dp0"

set "FOLDER_NAME=DeliveryControl"

echo Mengambil file project di %cd%...
dotnet publish "DeliveryControl.csproj" -c Release -o "./%FOLDER_NAME%"
echo.
echo Selesai! File siap di-deploy ada di folder: %cd%\%FOLDER_NAME%
pause
