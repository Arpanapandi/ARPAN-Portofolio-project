import sqlite3, os

# Coba semua database yang ada
dbs = ['delivery_test.db', 'DeliveryControl.db', 'delivery_dev.db']

for db in dbs:
    if not os.path.exists(db):
        continue
    conn = sqlite3.connect(db)
    c = conn.cursor()

    # Cek apakah tabel ada
    c.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='DeliverySchedules'")
    if not c.fetchone():
        print(f'{db}: tabel DeliverySchedules tidak ada')
        conn.close()
        continue

    c.execute('''
        SELECT s.ScheduleId, s.ScheduleNumber, s.ScheduledDate, s.CreatedDate, s.CreatedBy, s.Status,
               COUNT(di.DeliveryItemId) as ItemCount
        FROM DeliverySchedules s
        LEFT JOIN DeliveryItems di ON s.ScheduleId = di.ScheduleId
        GROUP BY s.ScheduleId
        HAVING ItemCount = 0
        ORDER BY s.CreatedDate DESC
        LIMIT 20
    ''')
    ghosts = c.fetchall()
    
    c.execute('SELECT COUNT(*) FROM DeliverySchedules')
    total = c.fetchone()[0]
    
    print(f'\n=== {db} ===')
    print(f'Total schedules: {total}, Ghost schedules: {len(ghosts)}')
    for r in ghosts:
        print(' ', r)

    # Cek total DeliveryItems juga
    c.execute('SELECT COUNT(*) FROM DeliveryItems')
    total_items = c.fetchone()[0]
    print(f'Total DeliveryItems: {total_items}')

    conn.close()
