import sqlite3, shutil, os

src = r'C:\$RECYCLE.BIN\S-1-5-21-3769443046-4072118098-557369967-57369\$RMA6ZIJ\DeliveryControl\DeliveryControl.db'
dst = r'C:\DeliveryControl_backup\DeliveryControl\recycle_test.db'

try:
    shutil.copy2(src, dst)
    print('Copy dari Recycle Bin OK')
except Exception as e:
    print('Copy gagal:', e)
    exit()

conn = sqlite3.connect(dst)
cur = conn.cursor()
cur.execute('SELECT COUNT(*) FROM PullingRecords')
print('PullingRecords:', cur.fetchone()[0])
cur.execute('SELECT MAX(CreatedDate) FROM PullingRecords')
print('Latest Pulling:', cur.fetchone()[0])
cur.execute('SELECT COUNT(*) FROM Items')
print('Items:', cur.fetchone()[0])
cur.execute('SELECT COUNT(*) FROM PreparationRecords')
print('PreparationRecords:', cur.fetchone()[0])
conn.close()
