using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Models;
using DeliveryControl.Data;
using BCrypt.Net;
using System.ComponentModel.DataAnnotations;

namespace DeliveryControl.Controllers
{
    public class AccountController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AccountController> _logger;

        public AccountController(ApplicationDbContext context, ILogger<AccountController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // GET: Account/Login
        public IActionResult Login()
        {
            // Jika sudah login, redirect berdasarkan Role
            if (HttpContext.Session.GetString("UserId") != null)
            {
                return RedirectAfterLogin(
                    HttpContext.Session.GetString("Username") ?? "",
                    HttpContext.Session.GetString("Role") ?? "");
            }
            return View();
        }

        // POST: Account/Login
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == model.Username && u.IsActive);

            if (user == null || string.IsNullOrEmpty(user.Password) || !BCrypt.Net.BCrypt.Verify(model.Password, user.Password))
            {
                ModelState.AddModelError("", "Username atau password salah");
                return View(model);
            }

            // Set session
            HttpContext.Session.SetString("UserId", user.UserId.ToString());
            HttpContext.Session.SetString("Username", user.Username);
            HttpContext.Session.SetString("FullName", user.FullName);
            
            // Hardcode Super Admin role based on username
            if (user.Username.ToLower() == "superadmin")
            {
                HttpContext.Session.SetString("Role", "Super Admin");
            }
            else
            {
                HttpContext.Session.SetString("Role", user.Role);
            }

            _logger.LogInformation($"User {user.Username} (Role: {HttpContext.Session.GetString("Role")}) logged in successfully");

            return RedirectAfterLogin(user.Username, user.Role);
        }

        private IActionResult RedirectAfterLogin(string username, string role)
        {
            // Username "driver" atau Role "Driver" → Driver Portal
            if (string.Equals(username, "driver", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(role, "Driver", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Driver");

            // Role Leader → Portal Preparation
            if (string.Equals(role, "Leader", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Preparation");

            // Role Preparation → Langsung ke halaman scan
            if (string.Equals(role, "Preparation", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "PreparationWorkflow");

            // Role Pulling → Portal Pulling
            if (string.Equals(role, "Pulling", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Index", "Pulling");

            // Role STO → Portal Opname
            if (string.Equals(role, "STO", StringComparison.OrdinalIgnoreCase))
                return RedirectToAction("Opname", "Stock");

            // Admin, Super Admin, User → Dashboard Shipping
            return RedirectToAction("Index", "Home");
        }

        // GET: Account/Logout
        public IActionResult Logout()
        {
            var username = HttpContext.Session.GetString("Username");
            HttpContext.Session.Clear();
            _logger.LogInformation($"User {username} logged out");
            return RedirectToAction("Login");
        }

        // GET: Account/AccessDenied
        public IActionResult AccessDenied()
        {
            var errorMessage = HttpContext.Session.GetString("ErrorMessage");
            if (!string.IsNullOrEmpty(errorMessage))
            {
                TempData["ErrorMessage"] = errorMessage;
                HttpContext.Session.Remove("ErrorMessage");
            }
            return View();
        }
    }

    // ViewModel untuk Login
    public class LoginViewModel
    {
        [Required(ErrorMessage = "Username wajib diisi")]
        [Display(Name = "Username")]
        public string Username { get; set; } = string.Empty;

        [Required(ErrorMessage = "Password wajib diisi")]
        [DataType(DataType.Password)]
        [Display(Name = "Password")]
        public string Password { get; set; } = string.Empty;
    }
}

