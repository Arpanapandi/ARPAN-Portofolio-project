import sqlite3
conn = sqlite3.connect('delivery_dev.db')
cur = conn.cursor()
cur.execute('SELECT ScheduleId, ScheduleNumber, ScheduledDate, PickupTime FROM DeliverySchedules LIMIT 10')
for row in cur.fetchall():
    print(row)
conn.close()
