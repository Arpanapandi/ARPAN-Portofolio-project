# Cara Mengecek Data ImageUrl di Database

## Opsi 1: Menggunakan Endpoint API (Recommended)

1. **Jalankan aplikasi:**
   ```powershell
   dotnet run
   ```

2. **Buka browser atau gunakan PowerShell script:**
   ```powershell
   # Jalankan script PowerShell
   powershell -ExecutionPolicy Bypass -File .\check-database-images.ps1
   ```

3. **Atau buka langsung di browser:**
   ```
   http://localhost:6001/Admin/CheckMachineImages
   ```

## Opsi 2: Menggunakan SQL Query

1. **Buka SQL Server Management Studio (SSMS)** atau tool database lainnya

2. **Connect ke database:**
   - Server: `10.14.149.34`
   - Database: `OeeSystemDb`
   - User: `usrvelasto`
   - Password: `H1s@na2025!!`

3. **Jalankan query dari file `check-machine-images.sql`**

## Opsi 3: Menggunakan sqlcmd (Command Line)

```powershell
sqlcmd -S 10.14.149.34 -d OeeSystemDb -U usrvelasto -P "H1s@na2025!!" -i check-machine-images.sql
```

## Data yang Akan Ditampilkan

- Total jumlah machines
- Jumlah machines dengan ImageUrl
- Jumlah machines tanpa ImageUrl
- Detail ImageUrl untuk setiap machine

## Troubleshooting

Jika endpoint tidak bisa diakses:
1. Pastikan aplikasi sedang berjalan
2. Pastikan port 6001 tidak digunakan aplikasi lain
3. Cek firewall settings

Jika SQL query tidak bisa dijalankan:
1. Pastikan SQL Server bisa diakses dari komputer Anda
2. Pastikan credentials benar
3. Cek network connectivity

