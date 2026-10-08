@echo off
echo ========================================
echo   STOP DAN START APLIKASI
echo ========================================
echo.

echo [1/4] Menghentikan aplikasi yang sedang jalan...
FOR /F "tokens=5" %%P IN ('netstat -ano ^| findstr ":5206" ^| findstr "LISTENING"') DO (
    echo Menghentikan process ID: %%P
    taskkill /F /PID %%P 2>nul
)
timeout /t 2 >nul

echo.
echo [2/4] Menghapus cache build...
if exist bin rmdir /s /q bin
if exist obj rmdir /s /q obj
echo Cache dihapus!

echo.
echo [3/4] Building aplikasi...
dotnet build
if errorlevel 1 (
    echo.
    echo [ERROR] Build gagal! Perbaiki error di atas.
    pause
    exit /b 1
)

echo.
echo [4/4] Menjalankan aplikasi...
echo.
echo ========================================
echo  APLIKASI BERJALAN DI:
echo  http://localhost:5206
echo ========================================
echo.
echo JANGAN LUPA: Tekan Ctrl+Shift+R di browser!
echo.
echo Tekan Ctrl+C untuk stop aplikasi
echo ========================================
echo.

dotnet run --launch-profile production

