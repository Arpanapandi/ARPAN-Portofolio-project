using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;

var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
optionsBuilder.UseSqlite(""Data Source=delivery_control_local.db;Cache=Shared"");
using (var db = new ApplicationDbContext(optionsBuilder.Options))
{
    var activeCount = db.Items.Count(i => !i.IsDeleted && i.IsActive);
    var inactiveCount = db.Items.Count(i => !i.IsDeleted && !i.IsActive);
    var totalCount = db.Items.Count(i => !i.IsDeleted);
    Console.WriteLine($""Active: {activeCount}, Inactive: {inactiveCount}, Total: {totalCount}"");
}
