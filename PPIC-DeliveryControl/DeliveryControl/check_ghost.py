import sqlite3

conn = sqlite3.connect('delivery_test.db')
c = conn.cursor()

# Ghost schedules: ScheduleId yang tidak punya DeliveryItems
c.execute('''
SELECT s.ScheduleId, s.ScheduleNumber, s.ScheduledDate, s.CreatedDate, s.CreatedBy, s.Status,
       COUNT(di.DeliveryItemId) as ItemCount
FROM DeliverySchedules s
LEFT JOIN DeliveryItems di ON s.ScheduleId = di.ScheduleId
GROUP BY s.ScheduleId
HAVING ItemCount = 0
ORDER BY s.CreatedDate DESC
LIMIT 30
''')
rows = c.fetchall()
print(f'Total ghost schedules: {len(rows)}')
for r in rows:
    print(r)

print()

# Semua schedule hari ini (28 April 2026) untuk konteks
c.execute('''
SELECT s.ScheduleId, s.ScheduleNumber, s.ScheduledDate, s.CreatedDate, s.CreatedBy, s.Status,
       COUNT(di.DeliveryItemId) as ItemCount
FROM DeliverySchedules s
LEFT JOIN DeliveryItems di ON s.ScheduleId = di.ScheduleId
WHERE date(s.ScheduledDate) = '2026-04-28'
GROUP BY s.ScheduleId
ORDER BY s.CreatedDate
''')
rows2 = c.fetchall()
print(f'Total schedules for 2026-04-28: {len(rows2)}')
for r in rows2:
    print(r)

conn.close()
