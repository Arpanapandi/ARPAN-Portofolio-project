@echo off
:: Batch script untuk membuka akses Firewall Port 5000 & 5131 (Dashboard Klinik)
:: Menjalankan dengan hak Administrator

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [!] Meminta hak akses Administrator...
    powershell -Command "Start-Process '%~f0' -Verb RunAs"
    exit /b
)

echo =======================================================
echo   KONFIGURASI FIREWALL DASHBOARD KLINIK
echo =======================================================
echo.
echo Menambahkan aturan Inbound Firewall untuk Port 5000 dan 5131 (TCP)...

netsh advfirewall firewall delete rule name="Dashboard Klinik Web (Port 5000 & 5131)" >nul 2>&1

netsh advfirewall firewall add rule name="Dashboard Klinik Web (Port 5000 & 5131)" dir=in action=allow protocol=TCP localport=5000,5131

if %errorLevel% equ 0 (
    echo.
    echo [SUKSES] Aturan Firewall berhasil ditambahkan!
    echo.
    echo Aplikasi sekarang dapat diakses dari device/HP lain melalui jaringan Wi-Fi/LAN.
) else (
    echo.
    echo [GAGAL] Terjadi kesalahan saat menambahkan aturan Firewall.
)

echo.
pause
