using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using DeliveryControl.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

class ProgramTest
{
    static async Task Main(string[] args)
    {
        var builder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json");
        var configuration = builder.Build();

        var services = new ServiceCollection();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlite(configuration.GetConnectionString("DefaultConnection")));
        
        var provider = services.BuildServiceProvider();
        var context = provider.GetRequiredService<ApplicationDbContext>();

        var items = await context.DeliveryItems
            .Include(d => d.Item)
            .Include(d => d.DeliverySchedule)
            .Where(d => d.Item != null && d.Item.VIN == "NA1660")
            .Take(10)
            .ToListAsync();

        foreach (var item in items)
        {
            Console.WriteLine($"Schedule: {item.DeliverySchedule?.ScheduleNumber}, VIN: {item.Item?.VIN}, ExtPartNo: {item.ExternalPartNo}, CustPartNo: {item.Item?.CustomerPartNumber}");
        }
    }
}
