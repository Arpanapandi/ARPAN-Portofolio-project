using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;

namespace CheckDB {
    class Program {
        static void Main() {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite("Data Source=C:\\DeliveryControl_backup_old\\DeliveryControl\\DeliveryControl.db")
                .Options;
            using var db = new ApplicationDbContext(options);
            var regulerItems = db.Items.Where(i => i.StatusItem == "Reguler" && i.IsDeleted == false).ToList();
            Console.WriteLine($"Total Reguler Items in DB: {regulerItems.Count}");
            
            var byCust = regulerItems.GroupBy(i => i.Customer).Select(g => $"{g.Key}: {g.Count()}").ToList();
            Console.WriteLine("By Customer:");
            foreach(var c in byCust) Console.WriteLine(c);
        }
    }
}
