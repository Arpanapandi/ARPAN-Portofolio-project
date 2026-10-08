
import re

with open("c:/DeliveryControl_backup_old/DeliveryControl/Controllers/StockController.cs", "r", encoding="utf-8") as f:
    code = f.read()

# Fix 2: Remove clearing of the NEW rack
pattern = r"(\s*// 1b\. Bersihkan SEMUA record aktif di rak TUJUAN.*?r\.AdjustNote = baseNote \+ clearNote;\s*})"
replacement = r"""
                    // [FIX] Jangan bersihkan record di rak TUJUAN. Jika user memindah rak, 
                    // stok tersebut harus DITAMBAHKAN ke rak tujuan tanpa menghapus stok yang sudah ada di sana.
                    // (Menghapus rak tujuan akan menyebabkan data hilang jika sebelumnya sudah ada item di rak tujuan)."""

code, count = re.subn(pattern, replacement, code, flags=re.DOTALL)
print(f"Replaced {count} instances of target 2")

with open("c:/DeliveryControl_backup_old/DeliveryControl/Controllers/StockController.cs", "w", encoding="utf-8") as f:
    f.write(code)
print("Done")

