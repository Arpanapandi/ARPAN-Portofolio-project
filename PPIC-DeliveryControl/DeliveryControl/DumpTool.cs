using System;
using System.Linq;
using DeliveryControl.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DeliveryControl
{
    public class DumpTool
    {
        public static void Run(string[] args)
        {
            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseSqlite("Data Source=DeliveryControl.db");
            using var context = new ApplicationDbContext(optionsBuilder.Options);

            var items = context.Items.Take(5).ToList();
            foreach (var item in items)
            {
                Console.WriteLine($"ID: {item.ItemId}, Code: '{item.ItemCode}', VIN: '{item.VIN}'");
            }
        }
    }
}
