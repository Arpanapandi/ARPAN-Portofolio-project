
import re

with open("c:/DeliveryControl_backup_old/DeliveryControl/Controllers/StockController.cs", "r", encoding="utf-8") as f:
    code = f.read()

# Fix 1: Add Label = ... + "-ZERO" to all zero stock markers
code = re.sub(
    r"(Tag\s*=\s*tag,\s*)(Plant\s*=\s*itemPlant,\s*)(Rack\s*=\s*newRack,\s*)(Column\s*=\s*newNoRack,\s*)(Quantity\s*=\s*0,)",
    r"\1Label          = tag + \"-ZERO\",\n                            \2\3\4\5",
    code
)

code = re.sub(
    r"(Tag\s*=\s*tag,\s*)(Plant\s*=\s*itemPlant,\s*)(Rack\s*=\s*oldRack,\s*)(Column\s*=\s*oldNoRack,\s*)(Quantity\s*=\s*0,)",
    r"\1Label          = tag + \"-ZERO\",\n                            \2\3\4\5",
    code
)

code = re.sub(
    r"(Tag\s*=\s*item\.VIN,\s*)(Plant\s*=\s*item\.Plant\s*\?\?\s*\"Unknown\",\s*)(Rack\s*=\s*rackStr,\s*)(Column\s*=\s*currentNoRack,\s*)(Quantity\s*=\s*0,)",
    r"\1Label          = item.VIN + \"-ZERO\",\n                                        \2\3\4\5",
    code
)

# Fix 2: Remove clearing of the NEW rack
target_to_remove = """                    // 1b. Bersihkan SEMUA record aktif di rak TUJUAN untuk item yang sama ? Mismatch
                    //     Tanpa ini, stock di rak tujuan menumpuk (5 lama + 10 baru = 15).
                    //     Opname adalah operasi absolut: hasil akhir di rak tujuan = newQty.
                    var allAtNewRack = await _context.PullingRecords
                        .Where(p => p.Tag == tag
                                 && (p.Rack ?? "").Trim().ToUpper() == newRack
                                 && p.Column == newNoRack
                                 && p.Remark != "Mismatch")
                        .ToListAsync();

                    foreach (var r in allAtNewRack)
                    {
                        r.Remark = "Mismatch";
                        var clearNote = " | Cleared by Opname (absolute reset)";
                        var baseNote = r.AdjustNote ?? "";
                        if (baseNote.Length + clearNote.Length > 200) baseNote = baseNote.Substring(0, 200 - clearNote.Length);
                        r.AdjustNote = baseNote + clearNote;
                    }"""

replacement = """                    // [FIX] Jangan bersihkan record di rak TUJUAN. Jika user memindah rak, 
                    // stok tersebut harus DITAMBAHKAN ke rak tujuan tanpa menghapus stok yang sudah ada di sana."""
                    
if target_to_remove in code:
    code = code.replace(target_to_remove, replacement)
else:
    print("Could not find target_to_remove")

# Fix 3: Remove diff == 0 check
target_to_remove_2 = """                    int diff = newQty - act;
                    if (diff == 0) continue;"""
                    
replacement_2 = """                    // [FIX] Hapus pengecekan diff == 0. 
                    // Kita harus selalu melakukan absolute reset tanpa mempedulikan nilai 'act' (stok di layar user).
                    // Hal ini mencegah kegagalan Opname jika stok asli di database diam-diam berubah (stale data)."""
                    
if target_to_remove_2 in code:
    code = code.replace(target_to_remove_2, replacement_2)
else:
    print("Could not find target_to_remove_2")

with open("c:/DeliveryControl_backup_old/DeliveryControl/Controllers/StockController.cs", "w", encoding="utf-8") as f:
    f.write(code)
print("Done")

