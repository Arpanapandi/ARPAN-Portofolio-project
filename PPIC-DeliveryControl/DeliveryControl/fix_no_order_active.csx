using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;

var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
optionsBuilder.UseSqlite("Data Source=DeliveryControl.db");
using var db = new ApplicationDbContext(optionsBuilder.Options);

var items = db.Items.Where(i => i.StatusItem == "No Order" && i.IsActive == false).ToList();
foreach(var i in items) {
    i.IsActive = true;
}
db.SaveChanges();
Console.WriteLine("Fixed " + items.Count + " items.");
