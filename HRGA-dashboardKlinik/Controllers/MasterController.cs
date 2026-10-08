using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using dashboardKlinik.Data;
using dashboardKlinik.Models;
using dashboardKlinik.Services;


namespace dashboardKlinik.Controllers
{
    [Authorize]
    public class MasterController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ActivityLogService _activityLog;
        private readonly ILogger<MasterController> _logger;

        public MasterController(
            ApplicationDbContext context,
            ActivityLogService activityLog,
            ILogger<MasterController> logger)
        {
            _context = context;
            _activityLog = activityLog;
            _logger = logger;
        }

        // GET: /Master/ManagementSetting
        [HttpGet]
        public async Task<IActionResult> ManagementSetting(string? searchString)
        {
            if (!User.IsInRole("Admin")) return RedirectToAction("Index", "Home");

            var query = _context.Users.AsQueryable();

            if (!string.IsNullOrWhiteSpace(searchString))
            {
                var term = searchString.Trim().ToLower();
                query = query.Where(u =>
                    u.Nama.ToLower().Contains(term) ||
                    u.NPK.Contains(term) ||
                    u.Username.ToLower().Contains(term) ||
                    u.Role.ToLower().Contains(term) ||
                    u.Status.ToLower().Contains(term));
            }

            ViewData["CurrentSearch"] = searchString;

            var users = await query
                .OrderBy(u => u.Id)
                .ToListAsync();

            return View(users);//crusial Code
        }

        // GET: /Master/GetUser/5 (JSON endpoint for edit modal)
        [HttpGet]
        public async Task<IActionResult> GetUser(int id)
        {
            if (!User.IsInRole("Admin")) return Forbid();

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound(new { success = false, message = "User tidak ditemukan" });
            }

            return Json(new
            {
                success = true,
                data = new
                {
                    id = user.Id,
                    nama = user.Nama,
                    username = user.Username,
                    npk = user.NPK,
                    role = user.Role,
                    status = user.Status
                }
            }); // crucal code 
        }

        // POST: /Master/SaveUser
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveUser([FromForm] UserSaveInputModel input)
        {
            if (!User.IsInRole("Admin")) return RedirectToAction("Index", "Home");

            if (input == null)
            {
                TempData["ErrorMessage"] = "Data tidak valid.";
                return RedirectToAction(nameof(ManagementSetting));
            }

            // Validasi NPK 4 digit angka
            if (string.IsNullOrWhiteSpace(input.NPK) || !Regex.IsMatch(input.NPK.Trim(), @"^\d{4}$"))
            {
                TempData["ErrorMessage"] = "NPK harus tepat 4 digit angka (contoh: 1001).";
                return RedirectToAction(nameof(ManagementSetting));
            }

            if (string.IsNullOrWhiteSpace(input.Nama))
            {
                TempData["ErrorMessage"] = "Nama lengkap wajib diisi.";
                return RedirectToAction(nameof(ManagementSetting));
            }

            if (string.IsNullOrWhiteSpace(input.Username))
            {
                TempData["ErrorMessage"] = "Username wajib diisi.";
                return RedirectToAction(nameof(ManagementSetting));
            }

            var trimmedNpk = input.NPK.Trim();
            var trimmedUsername = input.Username.Trim().ToLower();

            // Mode EDIT
            if (input.Id.HasValue && input.Id.Value > 0)
            {
                var user = await _context.Users.FindAsync(input.Id.Value);
                if (user == null)
                {
                    TempData["ErrorMessage"] = "Pengguna tidak ditemukan.";
                    return RedirectToAction(nameof(ManagementSetting));
                }

                // Cek duplikasi username (kecuali milik sendiri)
                var usernameExists = await _context.Users
                    .AnyAsync(u => u.Id != user.Id && u.Username.ToLower() == trimmedUsername);
                if (usernameExists)
                {
                    TempData["ErrorMessage"] = $"Username '{input.Username}' sudah digunakan oleh akun lain.";
                    return RedirectToAction(nameof(ManagementSetting));
                }

                user.Nama = input.Nama.Trim();
                user.Username = input.Username.Trim();
                user.NPK = trimmedNpk;
                user.Role = input.Role ?? "Admin";
                user.Status = input.Status ?? "Aktif";

                // Update password jika diisi
                if (!string.IsNullOrWhiteSpace(input.Password))
                {
                    user.Password = input.Password.Trim();
                }

                await _context.SaveChangesAsync();
                await _activityLog.LogAsync("UPDATE_USER", $"Update data user: {user.Nama} ({user.NPK})", "User", user.Id);
                TempData["SuccessMessage"] = $"Data user '{user.Nama}' berhasil diperbarui!";
            }
            // Mode CREATE
            else
            {
                if (string.IsNullOrWhiteSpace(input.Password))
                {
                    TempData["ErrorMessage"] = "Password wajib diisi untuk user baru.";
                    return RedirectToAction(nameof(ManagementSetting));
                }

                var usernameExists = await _context.Users
                    .AnyAsync(u => u.Username.ToLower() == trimmedUsername);
                if (usernameExists)
                {
                    TempData["ErrorMessage"] = $"Username '{input.Username}' sudah digunakan.";
                    return RedirectToAction(nameof(ManagementSetting));
                }

                var newUser = new User
                {
                    Nama = input.Nama.Trim(),
                    Username = input.Username.Trim(),
                    Password = input.Password.Trim(),
                    NPK = trimmedNpk,
                    Role = input.Role ?? "Admin",
                    Status = input.Status ?? "Aktif",
                    TanggalDibuat = DateTime.Now
                };

                _context.Users.Add(newUser);
                await _context.SaveChangesAsync();
                await _activityLog.LogAsync("CREATE_USER", $"Membuat user baru: {newUser.Nama} ({newUser.NPK})", "User", newUser.Id);
                TempData["SuccessMessage"] = $"Pengguna baru '{newUser.Nama}' berhasil ditambahkan!";
            }

            return RedirectToAction(nameof(ManagementSetting));
        }

        // POST: /Master/DeleteUser/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(int id)
        {
            if (!User.IsInRole("Admin")) return RedirectToAction("Index", "Home");

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User tidak ditemukan.";
                return RedirectToAction(nameof(ManagementSetting));
            }

            // Cegah hapus diri sendiri
            var currentUsername = User.Identity?.Name ?? "";
            if (user.Username.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "Anda tidak dapat menghapus akun yang sedang digunakan saat ini.";
                return RedirectToAction(nameof(ManagementSetting));
            }

            // Cegah hapus admin terakhir
            var adminCount = await _context.Users.CountAsync(u => u.Role == "Admin");
            if (user.Role == "Admin" && adminCount <= 1)
            {
                TempData["ErrorMessage"] = "Tidak dapat menghapus satu-satunya akun Administrator dalam sistem.";
                return RedirectToAction(nameof(ManagementSetting));
            }

            _context.Users.Remove(user);
            await _context.SaveChangesAsync();
            await _activityLog.LogAsync("DELETE_USER", $"Menghapus user: {user.Nama} ({user.NPK})", "User", id);
            TempData["SuccessMessage"] = $"User '{user.Nama}' berhasil dihapus.";

            return RedirectToAction(nameof(ManagementSetting));
        }
    }

    public class UserSaveInputModel
    {
        public int? Id { get; set; }
        public string Nama { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string? Password { get; set; }
        public string NPK { get; set; } = string.Empty;
        public string Role { get; set; } = "Admin";
        public string Status { get; set; } = "Aktif";
    }
}
