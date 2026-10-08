#r "nuget: Microsoft.EntityFrameworkCore, 8.0.0"
#r "nuget: Microsoft.EntityFrameworkCore.Design, 8.0.0"
#r "nuget: Microsoft.EntityFrameworkCore.SqlServer, 8.0.0"

using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;

// Setup
var optionsBuilder = new DbContextOptionsBuilder<DeliveryControl.Data.ApplicationDbContext>();
optionsBuilder.UseSqlServer("Server=tcp:s-jea-je-jein-sql01.database.windows.net,1433;Initial Catalog=DB_JEA_DELIVERY_CONTROL;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=\"Active Directory Default\";");
var context = new DeliveryControl.Data.ApplicationDbContext(optionsBuilder.Options);

var deletedItemVin = "58280KK1800012";
var pulling = context.PullingRecords.Where(r => r.Tag.Contains("58280KK18")).ToList();
Console.WriteLine($"Pulling records for 58280KK18: {pulling.Count}");
foreach(var r in pulling) {
    Console.WriteLine($"ID: {r.PullingId}, Rack: '{r.Rack}', Col: {r.Column}, Remark: '{r.Remark}', AdjustNote: '{r.AdjustNote}', Date: {r.CreatedDate}");
}
