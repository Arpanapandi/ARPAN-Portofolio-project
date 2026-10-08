import sqlite3

db = 'delivery_test.db'
conn = sqlite3.connect(db)
cur = conn.cursor()

# List tables
cur.execute("SELECT name FROM sqlite_master WHERE type='table'")
tables = [r[0] for r in cur.fetchall()]
print("Tables:", tables)

# Check customers
cur.execute("SELECT name FROM sqlite_master WHERE type='table' AND name LIKE '%ust%'")
print("Customer tables:", [r[0] for r in cur.fetchall()])

# Try to query delivery schedules
for tbl in tables:
    if 'Schedule' in tbl or 'schedule' in tbl or 'Delivery' in tbl:
        cur.execute(f"PRAGMA table_info({tbl})")
        cols = [r[1] for r in cur.fetchall()]
        print(f"\nTable {tbl}: {cols[:15]}")
        break

# Check carry-over schedules (scheduled before today, not completed)
cur.execute("""
SELECT s.ScheduleId, s.ScheduledDate, s.Cycle, s.Route,
       s.StartPrepareTime, s.StdPrepareTime, 
       s.EnterDockTime, s.PickupTime, 
       s.Status, s.ActualEndTime, s.PreparationStatus,
       c.CustomerCode, c.StartPrepareTime as cStartPrep, c.StdPrepareTime as cStdPrep,
       c.Docking, c.Pickup
FROM DeliverySchedules s
LEFT JOIN Customers c ON s.CustomerId = c.CustomerId
WHERE s.ScheduledDate < date('now') 
  AND s.Status != 'Completed'
  AND s.ActualEndTime IS NULL
LIMIT 20
""")
rows = cur.fetchall()
print(f"\n=== Carry-over schedules (not completed): {len(rows)} ===")
for r in rows:
    print(f"  ID={r[0]}, Date={r[1][:10]}, Cycle={r[2]}, Route={r[3]}")
    print(f"    StartPrep={r[4]}/{r[12]}, StdPrep={r[5]}/{r[13]}, DockIn={r[6]}, Pickup={r[7]}")
    print(f"    Status={r[8]}, PrepStatus={r[10]}, Cust={r[11]}")
    print(f"    Cust.Docking={r[14]}, Cust.Pickup={r[15]}")
    print()

conn.close()
