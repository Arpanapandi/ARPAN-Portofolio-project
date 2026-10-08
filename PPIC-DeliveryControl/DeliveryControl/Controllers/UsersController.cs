using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Models;
using DeliveryControl.Data;
using BCrypt.Net;
using Microsoft.AspNetCore.Mvc.Rendering;
using DeliveryControl.Filters;

namespace DeliveryControl.Controllers
{
    [AuthorizeAdmin]
    public class UsersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<UsersController> _logger;

        public UsersController(ApplicationDbContext context, ILogger<UsersController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // Helper method untuk check authentication dan authorization
        private bool IsAuthenticated()
        {
            return HttpContext.Session.GetString("UserId") != null;
        }

        private bool IsAdmin()
        {
            var role = HttpContext.Session.GetString("Role");
            return role == "Admin" || role == "Super Admin";
        }

        // GET: Users
        public async Task<IActionResult> Index()
        {
            if (!IsAuthenticated())
            {
                return RedirectToAction("Login", "Account");
            }

            if (!IsAdmin())
            {
                TempData["ErrorMessage"] = "Anda tidak memiliki akses untuk halaman ini";
                return RedirectToAction("Index", "Home");
            }

            var users = await _context.Users
                .Include(u => u.UserDockAccesses)
                    .ThenInclude(uda => uda.Dock)
                .OrderBy(u => u.Username)
                .ToListAsync();

            ViewBag.TotalDockCount = await _context.Docks.CountAsync();
            
            return View(users);
        }

        // GET: Users/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(m => m.UserId == id);
            
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // Helper: apakah current user adalah Super Admin
        private bool IsSuperAdmin()
        {
            var role = HttpContext.Session.GetString("Role");
            return role == "Super Admin";
        }

        // Helper: buat role list sesuai level akses
        private List<string> GetAvailableRoles()
        {
            var roles = new List<string> { "Admin", "User", "Pulling", "Preparation", "Leader", "Driver" };
            if (IsSuperAdmin())
                roles.Add("STO"); // Role STO hanya bisa dibuat oleh Super Admin
            return roles;
        }

        public async Task<IActionResult> Create()
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            var roles = GetAvailableRoles();
            ViewBag.Roles = new SelectList(roles);
            
            // Sync Docks from Customers if empty
            if (!await _context.Docks.AnyAsync())
            {
                var customers = await _context.Customers.ToListAsync();
                foreach (var cust in customers)
                {
                    _context.Docks.Add(new Dock
                    {
                        CustomerId = cust.CustomerId,
                        DockCode = cust.CustomerCode,
                        DockName = cust.CustomerName,
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    });
                }
                await _context.SaveChangesAsync();
            }

            // Load Docks for selection (deduplicate by DockName + CustomerCode + Cycle)
            ViewBag.AllDocks = (await _context.Docks
                .Include(d => d.Customer)
                .OrderBy(d => d.DockCode)
                .ToListAsync())
                .DistinctBy(d => (d.DockName, d.Customer?.CustomerCode, d.Customer?.Cycle))
                .ToList();


            return View();
        }

        // POST: Users/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Username,Password,FullName,Email,Role,IsActive,HasAllDockAccess,ConfirmPassword")] User user, int[] selectedDockIds)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            // Check username sudah ada
            if (await _context.Users.AnyAsync(u => u.Username == user.Username))
            {
                ModelState.AddModelError("Username", "Username sudah digunakan");
            }

            // Wajib isi password saat CREATE
            if (string.IsNullOrWhiteSpace(user.Password))
            {
                ModelState.AddModelError("Password", "Password wajib diisi");
            }

            if (ModelState.IsValid)
            {
                // Hash password
                user.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);
                user.CreatedDate = DateTime.Now;
                
                _context.Add(user);
                await _context.SaveChangesAsync();

                // Save Dock Access — hanya jika tidak HasAllDockAccess
                if (!user.HasAllDockAccess && selectedDockIds != null && selectedDockIds.Length > 0)
                {
                    foreach (var dockId in selectedDockIds)
                    {
                        _context.UserDockAccesses.Add(new UserDockAccess
                        {
                            UserId = user.UserId,
                            DockId = dockId,
                            CreatedDate = DateTime.Now
                        });
                    }
                    await _context.SaveChangesAsync();
                }
                
                TempData["SuccessMessage"] = "User berhasil ditambahkan";
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Roles = new SelectList(GetAvailableRoles(), user.Role);
            
            // Reload docks if validation fails
            ViewBag.AllDocks = (await _context.Docks.Include(d => d.Customer).OrderBy(d => d.DockCode).ToListAsync())
                .DistinctBy(d => (d.DockName, d.Customer?.CustomerCode, d.Customer?.Cycle)).ToList();
            ViewBag.SelectedDockIds = selectedDockIds ?? new int[0];

            return View(user);
        }

        // GET: Users/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Jangan tampilkan password yang sudah di-hash
            user.Password = "";
            ViewBag.Roles = new SelectList(GetAvailableRoles(), user.Role);

            // Sync Docks if empty
            if (!await _context.Docks.AnyAsync())
            {
                var customers_sync = await _context.Customers.ToListAsync();
                foreach (var cust in customers_sync)
                {
                    _context.Docks.Add(new Dock
                    {
                        CustomerId = cust.CustomerId,
                        DockCode = cust.CustomerCode,
                        DockName = cust.CustomerName,
                        IsActive = true,
                        CreatedDate = DateTime.Now
                    });
                }
                await _context.SaveChangesAsync();
            }

            // Load Docks and User Assignments (deduplicate by DockName + CustomerCode + Cycle)
            var allDocks = (await _context.Docks
                .Include(d => d.Customer)
                .OrderBy(d => d.DockCode)
                .ToListAsync())
                .DistinctBy(d => (d.DockName, d.Customer?.CustomerCode, d.Customer?.Cycle))
                .ToList();
            
            var userDockIds = await _context.UserDockAccesses
                .Where(uda => uda.UserId == id)
                .Select(uda => uda.DockId)
                .ToListAsync();

            ViewBag.AllDocks = allDocks;
            ViewBag.UserDockIds = userDockIds;

            return View(user);
        }

        // POST: Users/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("UserId,Username,Password,FullName,Email,Role,IsActive,HasAllDockAccess,ConfirmPassword")] User user, int[] selectedDockIds)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            if (id != user.UserId)
            {
                return NotFound();
            }

            // Check username sudah ada (kecuali user yang sedang diedit)
            if (await _context.Users.AnyAsync(u => u.Username == user.Username && u.UserId != id))
            {
                ModelState.AddModelError("Username", "Username sudah digunakan");
            }
            
            if (ModelState.IsValid)
            {
                try
                {
                    var existingUser = await _context.Users.FindAsync(id);
                    if (existingUser == null)
                    {
                        return NotFound();
                    }

                    // Update fields
                    existingUser.Username = user.Username;
                    existingUser.FullName = user.FullName;
                    existingUser.Email = user.Email;
                    existingUser.Role = user.Role;
                    existingUser.IsActive = user.IsActive;
                    existingUser.HasAllDockAccess = user.HasAllDockAccess;
                    existingUser.UpdatedDate = DateTime.Now;

                    // Update password hanya jika diisi
                    if (!string.IsNullOrWhiteSpace(user.Password))
                    {
                        existingUser.Password = BCrypt.Net.BCrypt.HashPassword(user.Password);
                    }

                    await _context.SaveChangesAsync();

                    // Update Dock Access — hanya jika tidak HasAllDockAccess
                    var existingAccess = await _context.UserDockAccesses
                        .Where(uda => uda.UserId == id)
                        .ToListAsync();
                    
                    _context.UserDockAccesses.RemoveRange(existingAccess);

                    // Jika HasAllDockAccess = true, tidak perlu simpan individual dock
                    // Jika HasAllDockAccess = false, simpan dock yang dipilih
                    if (!user.HasAllDockAccess && selectedDockIds != null && selectedDockIds.Length > 0)
                    {
                        foreach (var dockId in selectedDockIds)
                        {
                            _context.UserDockAccesses.Add(new UserDockAccess
                            {
                                UserId = id,
                                DockId = dockId,
                                CreatedDate = DateTime.Now
                            });
                        }
                    }

                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "User berhasil diupdate";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(user.UserId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }

            ViewBag.Roles = new SelectList(GetAvailableRoles(), user.Role);
            
            // Reload Docks if validation fails
            ViewBag.AllDocks = (await _context.Docks.Include(d => d.Customer).OrderBy(d => d.DockCode).ToListAsync())
                .DistinctBy(d => (d.DockName, d.Customer?.CustomerCode, d.Customer?.Cycle)).ToList();
            ViewBag.UserDockIds = selectedDockIds?.ToList() ?? new List<int>();

            return View(user);
        }

        // GET: Users/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.Users
                .FirstOrDefaultAsync(m => m.UserId == id);
            
            if (user == null)
            {
                return NotFound();
            }

            return View(user);
        }

        // POST: Users/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!IsAuthenticated() || !IsAdmin())
            {
                return RedirectToAction("Login", "Account");
            }

            var user = await _context.Users.FindAsync(id);
            if (user != null)
            {
                // Jangan hapus user yang sedang login
                var currentUserId = HttpContext.Session.GetString("UserId");
                if (currentUserId == user.UserId.ToString())
                {
                    TempData["ErrorMessage"] = "Tidak dapat menghapus user yang sedang login";
                    return RedirectToAction(nameof(Index));
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "User berhasil dihapus";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool UserExists(int id)
        {
            return _context.Users.Any(e => e.UserId == id);
        }
    }
}

