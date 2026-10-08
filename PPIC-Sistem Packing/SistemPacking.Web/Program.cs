using Microsoft.EntityFrameworkCore;
using SistemPacking.Web.Services;
using SistemPacking.Web.Interfaces;
using SistemPacking.Web.Data;

var builder = WebApplication.CreateBuilder(args);

// MVC
builder.Services.AddControllersWithViews();

// Database Provider configuration (SQLite for Development, SQL Server for Production)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    var connString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "";
    if (connString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
    {
        options.UseSqlite(connString);
    }
    else
    {
        options.UseSqlServer(
            connString,
            sqlOptions => sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null));
    }
});

// Unit of Work & Repository
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// Application Services
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IShoppingService, ShoppingService>();
builder.Services.AddScoped<IPackingService, PackingService>();
builder.Services.AddScoped<IVerificationService, VerificationService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IMasterExportImportService, MasterExportImportService>();
builder.Services.AddScoped<IShippingDashboardService, ShippingDashboardService>();

// Delivery Control Integration
builder.Services.AddHttpClient<IDeliveryControlIntegrationService, DeliveryControlIntegrationService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
    });

// Session
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.Name = ".SistemPacking.Session";
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// SignalR
builder.Services.AddSignalR();

// Anti-forgery
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

// EPPlus License
OfficeOpenXml.ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

var app = builder.Build();

// Auto migrate & seed on startup
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (db.Database.IsSqlite())
        {
            db.Database.EnsureCreated();
        }
        else
        {
            db.Database.Migrate();
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Perhatian: Gagal terhubung atau menjalankan migrasi database pada startup. Memproses aplikasi...");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

// SignalR Hubs
app.MapHub<SistemPacking.Web.Hubs.OrderHub>("/hubs/order");
app.MapHub<SistemPacking.Web.Hubs.DashboardHub>("/hubs/dashboard");

// Routes
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();
