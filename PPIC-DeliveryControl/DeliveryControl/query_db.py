import sqlite3

conn = sqlite3.connect('DeliveryControl.db')
c = conn.cursor()

print("--- PULLING RECORDS ---")
c.execute("SELECT PullingId, Tag, Label, Plant, CreatedDate, AdjustNote FROM PullingRecords WHERE Tag LIKE '%TA1700%' ORDER BY CreatedDate DESC LIMIT 5")
for row in c.fetchall():
    print(row)

print("--- PREPARATION RECORDS ---")
c.execute("SELECT PreparationId, Tag, Label, Kanban, CreatedDate FROM PreparationRecords WHERE Tag LIKE '%TA1700%' ORDER BY CreatedDate DESC LIMIT 5")
for row in c.fetchall():
    print(row)
