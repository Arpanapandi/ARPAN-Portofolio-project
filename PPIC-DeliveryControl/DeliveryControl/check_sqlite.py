import sqlite3, os

for db in ['DeliveryControl.db', 'delivery_dev.db']:
    if not os.path.exists(db):
        print(f"{db} not found"); continue
    conn = sqlite3.connect(db)
    c = conn.cursor()
    try:
        c.execute("SELECT COUNT(*) FROM PullingRecords")
        total = c.fetchone()[0]
        c.execute("SELECT COUNT(*) FROM PullingRecords WHERE CreatedDate >= '2026-04-15'")
        apr15 = c.fetchone()[0]
        c.execute("SELECT MIN(CreatedDate), MAX(CreatedDate) FROM PullingRecords")
        r = c.fetchone()
        print(f"{db}: Total Pulling={total}, Apr15+={apr15}, Range={r[0]} to {r[1]}")
        
        c.execute("SELECT COUNT(*) FROM PreparationRecords")
        total_p = c.fetchone()[0]
        print(f"{db}: Total Preparation={total_p}")
    except Exception as e:
        print(f"{db}: Error - {e}")
    conn.close()
