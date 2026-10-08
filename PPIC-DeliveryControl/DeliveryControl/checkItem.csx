using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

var builder = new ConfigurationBuilder().SetBasePath("C:\DeliveryControl_backup_old\DeliveryControl").AddJsonFile("appsettings.Development.json");
var configuration = builder.Build();
var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
optionsBuilder.UseSqlServer(configuration.GetConnectionString("DefaultConnection"));

using (var context = new ApplicationDbContext(optionsBuilder.Options))
{
    var items = context.Items.Where(x => x.PartNo == "45254-BZ140-00").ToList();
    foreach (var item in items)
    {
        Console.WriteLine($"Part: {item.PartNo}, QtyLot: {item.QtyLot}");
    }
}
