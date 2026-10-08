# Panduan Deployment ke IIS (Internet Information Services)

Dokumen ini menjelaskan langkah-langkah untuk menjalankan aplikasi **Delivery Control System** di web server IIS.

## 1. Prasyarat (Prerequisites)
Pastikan server tujuan sudah menginstal:
*   **IIS (Internet Information Services)** dengan fitur ASP.NET 4.8 dan WebSocket diaktifkan.
*   **.NET 8 Hosting Bundle** ([Download di sini](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)).
    *   *Penting:*Restart server (atau jalankan `iisreset`) setelah menginstal Hosting Bundle.

## 2. Persiapan Folder Aplikasi
1.  Siapkan folder tujuan di server, misalnya: `C:\inetpub\wwwroot\DeliveryControl`.
2.  Berikan izin akses (Permissions) untuk user IIS:
    *   Klik kanan folder -> **Properties** -> **Security**.
    *   Klik **Edit** -> **Add**.
    *   Ketik `IIS_IUSRS`, klik **Check Names**, lalu **OK**.
    *   Berikan hak akses **Read & Execute**, **List folder contents**, dan **Read**.
    *   *Catatan:* Jika aplikasi perlu menulis file (seperti log atau database SQLite), berikan akses **Modify**.

## 3. Publish Aplikasi
Anda bisa menggunakan script otomatis `DEPLOY_IIS.bat` yang sudah disediakan di folder root aplikasi. Script ini akan:
1.  Melakukan pembersihan (*Clean*).
2.  Melakukan build dan publish ke folder `C:\Deploy\DeliveryControl_Published`.

Setelah script selesai, salin isi folder tersebut ke folder tujuan IIS Anda.

## 4. Konfigurasi di IIS Manager
1.  Buka **IIS Manager** (`inetmgr`).
2.  **Application Pool**:
    *   Klik **Application Pools** -> **Add Application Pool**.
    *   Name: `DeliveryControlPool`.
    *   .NET CLR Version: **No Managed Code**.
    *   Managed Pipeline Mode: **Integrated**.
    *   Klik **OK**.
3.  **Add Website**:
    *   Klik kanan **Sites** -> **Add Website**.
    *   Site name: `DeliveryControl`.
    *   Application pool: Pilih `DeliveryControlPool`.
    *   Physical path: Arahkan ke folder tujuan (misal `C:\inetpub\wwwroot\DeliveryControl`).
    *   Binding: Tentukan port (misal port `80` atau port lain yang tersedia).
    *   Klik **OK**.

## 5. Konfigurasi Database (PENTING)
Secara default, aplikasi menggunakan SQLite. Jika Anda ingin beralih ke **SQL Server Production**:
1.  Buka `appsettings.Production.json` di folder publish.
2.  Sesuaikan `ConnectionStrings` ke SQL Server Anda.
3.  Pastikan `ASPNETCORE_ENVIRONMENT` sudah diatur ke `Production` di setting website IIS (lewat menu *Configuration Editor* atau di file `web.config`).

---

**Selamat! Aplikasi Anda sekarang sudah online di IIS.** 🚀
