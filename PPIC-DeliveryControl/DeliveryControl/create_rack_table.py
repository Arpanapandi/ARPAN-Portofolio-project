import sqlite3
conn = sqlite3.connect('delivery_test.db')
c = conn.cursor()
c.execute('''CREATE TABLE IF NOT EXISTS ItemRackLocations (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    ItemId INTEGER NOT NULL,
    Rack TEXT NOT NULL,
    NoRack INTEGER NOT NULL DEFAULT 0,
    Plant TEXT,
    SortOrder INTEGER NOT NULL DEFAULT 0,
    FOREIGN KEY (ItemId) REFERENCES Items(ItemId) ON DELETE CASCADE
)''')
c.execute('CREATE INDEX IF NOT EXISTS IX_ItemRackLocations_ItemId ON ItemRackLocations(ItemId, Rack, NoRack)')
conn.commit()

# Verify
c.execute("SELECT name FROM sqlite_master WHERE type='table' AND name='ItemRackLocations'")
result = c.fetchone()
print(f"Table created: {result}")

c.execute("SELECT COUNT(*) FROM ItemRackLocations")
print(f"Row count: {c.fetchone()[0]}")
conn.close()
print("Done.")
