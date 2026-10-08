@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   Delivery Control - IIS Live Update
echo ===================================================
echo.

:: KONFIGURASI (Sesuaikan jika perlu)
set "SOURCE_DIR=%~dp0DeliveryControl"
set "DEST_DIR=C:\inetpub\wwwroot\DeliveryControl"
set "APP_POOL_NAME=DeliveryControl"

:: Cek Administrator
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [WARNING] Script mungkin butuh akses ADMINISTRATOR!
    echo Jika error, klik kanan -^> Run as Administrator
)

echo 1. Memeriksa folder source...
if not exist "%SOURCE_DIR%" (
    echo [ERROR] Folder source tidak ditemukan: %SOURCE_DIR%
    echo Harap jalankan DEPLOY_IIS.bat terlebih dahulu!
    pause
    exit /b
)

echo 2. Menghentikan Application Pool '%APP_POOL_NAME%'...
%systemroot%\system32\inetsrv\appcmd stop apppool /apppool.name:"%APP_POOL_NAME%"

:: Tunggu sebentar agar file lock lepas
timeout /t 2 /nobreak >nul

echo 3. Backup konfigurasi production saat ini...
if exist "%DEST_DIR%\appsettings.Production.json" (
    copy /y "%DEST_DIR%\appsettings.Production.json" "%TEMP%\appsettings.Production.json.bak" >nul
    echo Config di-backup ke %TEMP%.
)

echo 4. Menyalin file update...
:: Gunakan Robocopy untuk copy file (Mirroring - menghapus yang tidak ada di source, kecuali config tertentu jika di-exclude)
:: /MIR = Mirror directory tree
:: /XF = Exclude Files (Kita exclude appsettings.Production.json agar tidak tertimpa default dari build jika user sudah edit di server)
robocopy "%SOURCE_DIR%" "%DEST_DIR%" /MIR /IS /XF appsettings.Production.json

echo 5. Mengembalikan konfigurasi (jika perlu)...
:: Jika file config tidak ada di tujuan (baru deploy pertama), copy dari source
if not exist "%DEST_DIR%\appsettings.Production.json" (
    echo Menggunakan default config dari publish...
    copy /y "%SOURCE_DIR%\appsettings.Production.json" "%DEST_DIR%\appsettings.Production.json"
)

echo 6. Menyalakan kembali Application Pool...
%systemroot%\system32\inetsrv\appcmd start apppool /apppool.name:"%APP_POOL_NAME%"

echo.
echo ===================================================
echo   UPDATE SELESAI!
echo ===================================================
echo.
pause
