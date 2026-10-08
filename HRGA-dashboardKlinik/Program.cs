using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication.Cookies;
using dashboardKlinik.Data;
using dashboardKlinik.Hubs;
using dashboardKlinik.Models;
using dashboardKlinik.Services;

var builder = WebApplication.CreateBuilder(args);

// Konfigurasi Kestrel agar dapat diakses dari device lain (IP Network / 0.0.0.0)
builder.WebHost.UseUrls("http://0.0.0.0:5000;http://0.0.0.0:5131");

// Add services to the container.
builder.Services.AddControllersWithViews();

// Authentication with Cookies
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.Name = "KlinikAuthCookie";
        options.Cookie.HttpOnly = true;
    });

// SignalR
builder.Services.AddSignalR();

// Database configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=KlinikDB.db";

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (connectionString.Contains("Server=") || connectionString.Contains("DataSource="))
    {
        options.UseSqlServer(connectionString);
    }
    else
    {
        options.UseSqlite(connectionString);
    }
});

// Services
builder.Services.AddScoped<ActivityLogService>();

// HttpClientFactory for background service
builder.Services.AddHttpClient();

// Background Polling Service - Google Sheet → Database (real-time sync)
// DINONAKTIFKAN: Aplikasi kini menggunakan form web internal secara penuh
// builder.Services.AddHostedService<GoogleSheetPollingService>();

var app = builder.Build();

// Auto-migrate & Seed Default Admin
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        context.Database.Migrate();

        // Seed default admin user if none exists
        if (!context.Users.Any())
        {
            context.Users.Add(new User
            {
                Nama = "Administrator",
                Username = "admin",
                Password = "admin123",
                NPK = "1001",
                Role = "Admin",
                Status = "Aktif",
                TanggalDibuat = DateTime.Now
            });
            context.SaveChanges();
        }

        // Seed default dokter user if none exists
        if (!context.Users.Any(u => u.Role == "Dokter"))
        {
            context.Users.Add(new User
            {
                Nama = "dr. Budi Santoso",
                Username = "dokter",
                Password = "dokter123",
                NPK = "2001",
                Role = "Dokter",
                Status = "Aktif",
                TanggalDibuat = DateTime.Now
            });
            context.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while migrating the database.");
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Map SignalR Hub
app.MapHub<KlinikHub>("/klinikHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// Gunakan app.Run() tanpa parameter agar fleksibel (bisa IIS atau Kestrel port default)
app.Run();
