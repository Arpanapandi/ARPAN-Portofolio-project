using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using Microsoft.Extensions.DependencyInjection;

var options = new DbContextOptionsBuilder<ApplicationDbContext>()
    .UseSqlServer(""Server=10.14.149.34;Database=ppic_DeliveryControl;User Id=usrvelasto;Password=H1s@na2025!!;TrustServerCertificate=true;MultipleActiveResultSets=true"")
    .Options;
using var db = new ApplicationDbContext(options);

var dups = db.DeliverySchedules
    .GroupBy(d => d.ScheduleNumber)
    .Where(g => g.Count() > 1)
    .Select(g => new { g.Key, Count = g.Count() })
    .ToList();

foreach(var d in dups) {
    Console.WriteLine($""{d.Key}: {d.Count}"");
}
