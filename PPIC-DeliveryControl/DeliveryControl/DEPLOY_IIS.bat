@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   Delivery Control - IIS Deployment Automation
echo ===================================================
echo.

:: Tentukan folder Output
set PUBLISH_DIR=C:\Deploy\DeliveryControl_Published

echo 1. Membersihkan folder publish lama...
if exist "%PUBLISH_DIR%" (
    rmdir /s /q "%PUBLISH_DIR%"
)
mkdir "%PUBLISH_DIR%"

echo 2. Menjalankan 'dotnet clean' dan 'dotnet publish'...
dotnet clean -c Release
dotnet publish "DeliveryControl.csproj" -c Release -o "%PUBLISH_DIR%"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo [ERROR] Gagal melakukan publish aplikasi.
    pause
    exit /b %ERRORLEVEL%
)

echo.
echo ===================================================
echo   SUCCESS: Aplikasi berhasil di-publish!
echo ===================================================
echo Lokasi file: %PUBLISH_DIR%
echo.
echo Langkah selanjutnya:
echo 1. Salin isi folder di atas ke folder Website IIS Anda.
echo 2. Pastikan file 'appsettings.Production.json' sudah sesuai.
echo 3. Cek izin folder (IUSR / IIS_IUSRS).
echo.
echo Silakan baca 'IIS_DEPLOYMENT_GUIDE.md' untuk panduan detail.
echo ===================================================
pause
