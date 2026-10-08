using DeliveryControl.Data;
using Microsoft.EntityFrameworkCore;
var ctx = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite("Data Source=DeliveryControl.db").Options);
var items = ctx.Items.Where(i => i.VIN == "TA1680").ToList();
var itemIds = items.Select(i => i.ItemId).ToList();
var pulls = ctx.PullingRecords.Where(p => p.ItemId != null && itemIds.Contains(p.ItemId.Value) || (p.Tag != null && p.Tag == "TA1680")).ToList();
foreach(var p in pulls) {
    Console.WriteLine($"Id:{p.PullingId} Tag:{p.Tag} ItemId:{p.ItemId} Rack:{p.Rack}.{p.Column} Rem:{p.Remark} Adj:{p.AdjustNote} Date:{p.CreatedDate}");
}
