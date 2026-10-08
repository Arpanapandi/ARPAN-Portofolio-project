import sqlite3

conn = sqlite3.connect('DeliveryControl.db')
cur = conn.cursor()

# Print semua kolom yang ada
tables = [r[0] for r in cur.execute("SELECT name FROM sqlite_master WHERE type='table' ORDER BY name")]
print("=== EXISTING TABLES & COLUMNS ===")
for t in tables:
    cols = [r[1] for r in cur.execute(f'PRAGMA table_info("{t}")')]
    print(f"  {t}: {cols}")

print("\n=== APPLYING MISSING COLUMNS ===")

# Daftar kolom yang perlu ditambahkan: (table, column, type, default)
missing_columns = [
    ("Users",               "HasAllDockAccess",        "INTEGER", "0"),
    ("PullingRecords",      "IsManualAdjust",           "INTEGER", "0"),
    ("PullingRecords",      "AdjustNote",               "TEXT",    "NULL"),
    ("PullingRecords",      "Remark",                   "TEXT",    "'Match'"),
    ("PreparationRecords",  "Remark",                   "TEXT",    "'Match'"),
    ("DeliverySchedules",   "ActualEnterDockTime",      "TEXT",    "NULL"),
    ("DeliverySchedules",   "IsLeaderVerified",         "INTEGER", "0"),
    ("DeliverySchedules",   "LeaderVerifiedAt",         "TEXT",    "NULL"),
    ("DeliverySchedules",   "LeaderVerifiedBy",         "TEXT",    "NULL"),
    ("DeliverySchedules",   "LeaderVerifiedKanbanCount","INTEGER", "NULL"),
]

for (table, col, coltype, default) in missing_columns:
    # Cek apakah kolom sudah ada
    existing = [r[1] for r in cur.execute(f'PRAGMA table_info("{table}")')]
    if col not in existing:
        if default == "NULL":
            sql = f'ALTER TABLE "{table}" ADD COLUMN "{col}" {coltype}'
        else:
            sql = f'ALTER TABLE "{table}" ADD COLUMN "{col}" {coltype} NOT NULL DEFAULT {default}'
        print(f"  ADD: {table}.{col} ({coltype}) DEFAULT {default}")
        cur.execute(sql)
    else:
        print(f"  OK : {table}.{col} already exists")

conn.commit()
conn.close()
print("\nDone! SQLite updated successfully.")
