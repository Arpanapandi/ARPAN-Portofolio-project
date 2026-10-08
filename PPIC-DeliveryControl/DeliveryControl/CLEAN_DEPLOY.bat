@echo off
setlocal enabledelayedexpansion

echo ===================================================
echo   Delivery Control - CLEAN DEPLOYMENT (RESET)
echo ===================================================
echo.

set "SOURCE_DIR=C:\Deploy\DeliveryControl_Published"
set "DEST_DIR=C:\inetpub\wwwroot\DeliveryControl"
set "BACKUP_DIR=%TEMP%\DeliveryControl_Backup_%RANDOM%"

:: 1. Cek Folder Source
if not exist "%SOURCE_DIR%" (
    echo [ERROR] Folder source tidak ditemukan: %SOURCE_DIR%
    echo Harap jalankan DEPLOY_IIS.bat terlebih dahulu!
    pause
    exit /b 1
)

:: 2. Backup Config
echo 1. Membackup konfigurasi...
if not exist "%BACKUP_DIR%" mkdir "%BACKUP_DIR%"

if exist "%DEST_DIR%\appsettings.Production.json" (
    copy "%DEST_DIR%\appsettings.Production.json" "%BACKUP_DIR%\"
    echo Config Production dibackup ke %BACKUP_DIR%
)
if exist "%DEST_DIR%\web.config" (
    copy "%DEST_DIR%\web.config" "%BACKUP_DIR%\"
    echo web.config dibackup ke %BACKUP_DIR%
)

:: 3. Stop IIS
echo 2. Mematikan Application Pool...
%systemroot%\system32\inetsrv\appcmd stop apppool /apppool.name:"DeliveryControl"

:: Tunggu sebentar agar file release lock
timeout /t 5 /nobreak >nul

:: 4. HAPUS TOTAL (WIPE)
echo 3. MENGHAPUS TOTAL folder tujuan (WIPE)...
if exist "%DEST_DIR%" (
    rmdir /s /q "%DEST_DIR%"
)
:: Buat ulang folder kosong
mkdir "%DEST_DIR%"

:: 5. Copy Baru
echo 4. Menyalin file BARU...
xcopy "%SOURCE_DIR%\*" "%DEST_DIR%\" /E /H /C /I /Y

:: 6. Restore Config
echo 5. Mengembalikan konfigurasi...
if exist "%BACKUP_DIR%\appsettings.Production.json" (
    copy /Y "%BACKUP_DIR%\appsettings.Production.json" "%DEST_DIR%\"
)
if exist "%BACKUP_DIR%\web.config" (
    copy /Y "%BACKUP_DIR%\web.config" "%DEST_DIR%\"
)

:: 7. Start IIS
echo 6. Menyalakan kembali Application Pool...
%systemroot%\system32\inetsrv\appcmd start apppool /apppool.name:"DeliveryControl"

echo.
echo ===================================================
echo   CLEAN DEPLOY SUKSES!
echo ===================================================
echo Silakan cek browser Anda.
pause
