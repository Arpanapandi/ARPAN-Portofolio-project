using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Data;
using dashboardKlinik.Models;
using dashboardKlinik.Services;
using dashboardKlinik.ViewModels;

namespace dashboardKlinik.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _activityLog;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            ApplicationDbContext context,
            ActivityLogService activityLog,
            ILogger<AccountController> logger)
        {
            _context = context;
            _activityLog = activityLog;
            _logger = logger;
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
            {
                return RedirectToAction("Index", "Home");
            }

            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // Cari user di database
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username.ToLower() == model.Username.Trim().ToLower());

            // Fallback khusus user admin default jika DB baru di-init
            if (user == null && model.Username.Trim().ToLower() == "admin" && model.Password == "admin123")
            {
                user = new User
                {
                    Nama = "Administrator",
                    Username = "admin",
                    Password = "admin123",
                    NPK = "1001",
                    Role = "Admin",
                    Status = "Aktif",
                    TanggalDibuat = DateTime.Now
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }
            // Fallback khusus user dokter default jika belum terdaftar
            else if (user == null && model.Username.Trim().ToLower() == "dokter" && model.Password == "dokter123")
            {
                user = new User
                {
                    Nama = "dr. Budi Santoso",
                    Username = "dokter",
                    Password = "dokter123",
                    NPK = "2001",
                    Role = "Dokter",
                    Status = "Aktif",
                    TanggalDibuat = DateTime.Now
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }

            // Validasi password
            if (user == null || user.Password != model.Password)
            {
                ModelState.AddModelError(string.Empty, "Username atau Password yang Anda masukkan salah.");
                return View(model);
            }

            // Validasi status akun
            if (user.Status != "Aktif")
            {
                ModelState.AddModelError(string.Empty, "Akun Anda saat ini berstatus Non-Aktif. Hubungi Administrator.");
                return View(model);
            }

            // Buat Claims
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.Username),
                new Claim("FullName", user.Nama),
                new Claim("NPK", user.NPK),
                new Claim(ClaimTypes.Role, user.Role)
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = model.RememberMe,
                ExpiresUtc = model.RememberMe ? DateTimeOffset.UtcNow.AddDays(7) : DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            await _activityLog.LogAsync("LOGIN", $"User {user.Nama} ({user.Username}) berhasil login", "User", user.Id);
            _logger.LogInformation("User {Username} berhasil login", user.Username);

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
            {
                return Redirect(model.ReturnUrl);
            }

            return RedirectToAction("Index", "Home");
        }

        [HttpPost]
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            var username = User.Identity?.Name ?? "Unknown";
            await _activityLog.LogAsync("LOGOUT", $"User {username} logout dari sistem", "User");

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            _logger.LogInformation("User {Username} logged out", username);

            return RedirectToAction("Login", "Account");
        }

        [HttpPost]
        [HttpGet]
        [AllowAnonymous]
        public async Task<IActionResult> GuestLogin(int? ticketId = null)
        {
            if (ticketId.HasValue)
            {
                Response.Cookies.Append("ActiveTicketId", ticketId.Value.ToString(), new CookieOptions
                {
                    Expires = DateTimeOffset.Now.AddDays(1),
                    HttpOnly = false
                });
            }

            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, "0"),
                new Claim(ClaimTypes.Name, "guest"),
                new Claim("FullName", "Tamu (Guest)"),
                new Claim("NPK", "GUEST"),
                new Claim(ClaimTypes.Role, "Guest")
            };

            var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var authProperties = new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8)
            };

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(claimsIdentity),
                authProperties);

            await _activityLog.LogAsync("GUEST_LOGIN", "Masuk sebagai Guest (Tamu) tanpa login", "User", 0);
            _logger.LogInformation("User masuk sebagai Guest");

            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult AccessDenied()
        {
            return RedirectToAction("Login", "Account");
        }
    }
}
