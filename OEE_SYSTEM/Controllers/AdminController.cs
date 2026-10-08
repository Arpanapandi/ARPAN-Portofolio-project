using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Hosting;
using OeeSystem.Data;
using OeeSystem.Models;
using OeeSystem.Models.ViewModels;
using OeeSystem.Hubs;
using System.IO;
using System;
using ClosedXML.Excel;

namespace OeeSystem.Controllers;

public class AdminController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly IHubContext<OeeHub> _hubContext;
    private readonly IWebHostEnvironment _environment;

    public AdminController(ApplicationDbContext context, IHubContext<OeeHub> hubContext, IWebHostEnvironment environment)
    {
        _context = context;
        _hubContext = hubContext;
        _environment = environment;
    }

    // Helper method untuk reload plants dropdown
    private void ReloadPlantsDropdown(int? selectedPlantId = null)
    {
        var plantsList = _context.Plants.OrderBy(p => p.Name).ToList();
        var plantSelectListData = plantsList.Select(p => new { 
            Id = p.Id, 
            DisplayText = $"{p.Code} - {p.Name}" 
        }).ToList();
        ViewData["PlantId"] = new SelectList(plantSelectListData, "Id", "DisplayText", selectedPlantId);
    }

    // ========== MACHINES CRUD ==========
    // ✅ Temporary endpoint untuk cek data ImageUrl di database
    [HttpGet]
    public async Task<IActionResult> CheckMachineImages()
    {
        var machines = await _context.Machines
            .AsNoTracking()
            .Select(m => new
            {
                m.Id,
                m.Name,
                ImageUrl = m.ImageUrl ?? "NULL",
                ImageUrlLength = m.ImageUrl != null ? m.ImageUrl.Length : 0,
                HasImageUrl = !string.IsNullOrWhiteSpace(m.ImageUrl)
            })
            .OrderBy(m => m.Id)
            .ToListAsync();

        var summary = new
        {
            TotalMachines = machines.Count,
            MachinesWithImage = machines.Count(m => m.HasImageUrl),
            MachinesWithoutImage = machines.Count(m => !m.HasImageUrl),
            Machines = machines
        };

        return Json(summary);
    }

    public async Task<IActionResult> Machines()
    {
        // ✅ PERBAIKAN: Pastikan ImageUrl ter-load dengan benar dari database
        var machines = await _context.Machines
            .Include(m => m.Plant)
            .ToListAsync();
        
        // ✅ PERBAIKAN: Reload ImageUrl untuk setiap machine dari database untuk memastikan data fresh
        var machineIds = machines.Select(m => m.Id).ToList();
        var machineImageUrls = await _context.Machines
            .AsNoTracking()
            .Where(m => machineIds.Contains(m.Id))
            .Select(m => new { m.Id, m.ImageUrl })
            .ToDictionaryAsync(m => m.Id, m => m.ImageUrl);
        
        foreach (var machine in machines)
        {
            // Ambil ImageUrl langsung dari dictionary (data fresh dari database)
            if (machineImageUrls.TryGetValue(machine.Id, out var imageUrl) && !string.IsNullOrWhiteSpace(imageUrl))
            {
                machine.ImageUrl = imageUrl.Trim();
            }
            else
            {
                machine.ImageUrl = null;
            }
        }
        
        return View(machines);
    }

    public IActionResult CreateMachine()
    {
        // Ambil semua plants dari database untuk dropdown, diurutkan berdasarkan Name
        var plants = _context.Plants.OrderBy(p => p.Name).ToList();
        // Buat SelectList dengan menampilkan Code - Name
        var plantSelectList = plants.Select(p => new { 
            Id = p.Id, 
            DisplayText = $"{p.Code} - {p.Name}" 
        }).ToList();
        ViewData["PlantId"] = new SelectList(plantSelectList, "Id", "DisplayText");
        
        // Return view dengan model baru (Status akan menggunakan default value dari model)
        // Placeholder akan terpilih karena kita akan handle di view dengan JavaScript
        return View(new Machine());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateMachine(Machine machine, IFormFile? imageFile)
    {
        // Validasi ID tidak boleh kosong
        if (string.IsNullOrWhiteSpace(machine.Id))
        {
            ModelState.AddModelError("Id", "Machine ID wajib diisi");
        }
        else
        {
            // Cek apakah ID sudah digunakan
            var idExists = await _context.Machines.AnyAsync(m => m.Id == machine.Id);
            if (idExists)
            {
                ModelState.AddModelError("Id", "Machine ID sudah digunakan. Pilih ID lain.");
            }
        }
        
        // Validasi Status wajib dipilih
        var statusValue = Request.Form["Status"].ToString();
        if (string.IsNullOrEmpty(statusValue))
        {
            ModelState.AddModelError("Status", "Status wajib dipilih");
        }
        else
        {
            // Pastikan value yang dikirim valid
            if (!Enum.TryParse<MachineStatus>(statusValue, out var parsedStatus))
            {
                ModelState.AddModelError("Status", "Status tidak valid");
            }
            else
            {
                // Set status yang sudah di-parse
                machine.Status = parsedStatus;
            }
        }
        
        if (ModelState.IsValid)
        {
            // ✅ PERBAIKAN: Simpan image ke filesystem dan simpan path ke ImageUrl di database
            if (imageFile != null && imageFile.Length > 0)
            {
                // Validasi ukuran file (max 5MB)
                const long maxFileSize = 5 * 1024 * 1024; // 5MB
                if (imageFile.Length > maxFileSize)
                {
                    ModelState.AddModelError("imageFile", "Ukuran file maksimal 5MB");
                    ReloadPlantsDropdown(machine.PlantId);
                    return View(machine);
                }
                
                // Simpan file ke filesystem
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "machines");
                Directory.CreateDirectory(uploadsFolder);
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(imageFile.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);
                
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await imageFile.CopyToAsync(stream);
                }
                
                // Simpan path ke ImageUrl di database
                machine.ImageUrl = $"/images/machines/{fileName}";
            }

            _context.Add(machine);
            await _context.SaveChangesAsync();
            
            // Sinkronkan gambar produk yang terhubung dengan mesin ini (jika ada)
            await SyncProductsImageFromMachine(machine.Id);
            
            // ✅ PERBAIKAN: Broadcast SignalR untuk update status ke semua views
            await _hubContext.Clients.All.SendAsync("OeeUpdated", new
            {
                Type = "MachineStatusUpdated",
                MachineId = machine.Id,
                MachineName = machine.Name,
                MachineStatus = machine.Status.ToString(), // 'Aktif' atau 'TidakAktif'
                Message = $"Mesin baru {machine.Name} dibuat dengan status {machine.Status}",
                Timestamp = DateTime.Now,
                RefreshOperatorData = true, // Flag untuk trigger refresh Operator View
                RefreshDashboard = true, // Flag untuk trigger refresh Dashboard
                RefreshOeeDetail = true // Flag untuk trigger refresh OEE Detail
            });
            
            return RedirectToAction(nameof(Machines));
        }
        
        // Jika ada error validation, reload dropdown Plants dari database
        ReloadPlantsDropdown(machine.PlantId);
        return View(machine);
    }

    public async Task<IActionResult> EditMachine(string? id)
    {
        if (id == null) return NotFound();
        var machine = await _context.Machines
            .Include(m => m.Plant)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (machine == null) return NotFound();
        
        // Ambil semua plants dari database untuk dropdown, diurutkan berdasarkan Name
        var plants = await _context.Plants.OrderBy(p => p.Name).ToListAsync();
        // Buat SelectList dengan menampilkan Code - Name
        var plantSelectList = plants.Select(p => new { 
            Id = p.Id, 
            DisplayText = $"{p.Code} - {p.Name}" 
        }).ToList();
        ViewData["PlantId"] = new SelectList(plantSelectList, "Id", "DisplayText", machine.PlantId);
        ViewBag.PlantId = ViewData["PlantId"];
        
        return View(machine);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditMachine(string id, Machine machine, IFormFile? imageFile)
    {
        var existingMachine = await _context.Machines.FindAsync(id);
        if (existingMachine == null) return NotFound();
        
        // Validasi ID tidak boleh kosong
        if (string.IsNullOrWhiteSpace(machine.Id))
        {
            ModelState.AddModelError("Id", "Machine ID wajib diisi");
        }
        else
        {
            // Validasi panjang ID maksimal 4 karakter
            if (machine.Id.Length > 4)
            {
                ModelState.AddModelError("Id", "Machine ID maksimal 4 karakter");
            }
            else
            {
                // Jika ID diubah, cek apakah ID baru sudah digunakan oleh machine lain
                // (selain machine yang sedang diedit)
                if (id != machine.Id)
                {
                    var idExists = await _context.Machines
                        .AnyAsync(m => m.Id == machine.Id);
                    
                    if (idExists)
                    {
                        ModelState.AddModelError("Id", "Machine ID sudah digunakan. Pilih ID lain.");
                    }
                }
            }
        }
        
        // Validasi Status wajib dipilih
        var statusValue = Request.Form["Status"].ToString();
        if (string.IsNullOrEmpty(statusValue))
        {
            ModelState.AddModelError("Status", "Status wajib dipilih");
        }
        else
        {
            // Pastikan value yang dikirim valid
            if (!Enum.TryParse<MachineStatus>(statusValue, out var parsedStatus))
            {
                ModelState.AddModelError("Status", "Status tidak valid");
            }
            else
            {
                // Set status yang sudah di-parse
                machine.Status = parsedStatus;
            }
        }
        
        if (ModelState.IsValid)
        {
            try
            {
                // Jika ID diubah, perlu handle dengan cara khusus karena ID adalah primary key
                if (id != machine.Id)
                {
                    var oldId = id;
                    var newId = machine.Id;
                    
                    // Gunakan transaction untuk memastikan semua operasi berhasil atau rollback
                    using var transaction = await _context.Database.BeginTransactionAsync();
                    try
                    {
                        // Update foreign keys menggunakan parameterized query
                        await _context.Database.ExecuteSqlRawAsync(
                            "UPDATE JobRuns SET MachineId = {0} WHERE MachineId = {1}", 
                            newId, oldId);
                        
                        await _context.Database.ExecuteSqlRawAsync(
                            "UPDATE ProductMachines SET MachineId = {0} WHERE MachineId = {1}", 
                            newId, oldId);
                        
                        await _context.Database.ExecuteSqlRawAsync(
                            "UPDATE MachineDowntimeReasons SET MachineId = {0} WHERE MachineId = {1}", 
                            newId, oldId);
                        
                        // ✅ PERBAIKAN: Handle delete image dari filesystem dan database
                        var deleteImage = Request.Form.ContainsKey("deleteImage") && Request.Form["deleteImage"].ToString() == "true";
                        string finalImageUrl = existingMachine.ImageUrl ?? "";
                        
                        if (deleteImage && !string.IsNullOrEmpty(finalImageUrl))
                        {
                            // Hapus file dari filesystem
                            try
                            {
                                var imagePath = finalImageUrl.TrimStart('/');
                                var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", imagePath);
                                if (System.IO.File.Exists(fullPath))
                                {
                                    System.IO.File.Delete(fullPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error deleting image file: {ex.Message}");
                            }
                            
                            finalImageUrl = ""; // Set ke empty untuk di-set null nanti
                        }
                        
                        // ✅ PERBAIKAN: Handle image upload ke filesystem dan simpan path ke database
                        if (imageFile != null && imageFile.Length > 0)
                        {
                            // Validasi ukuran file (max 5MB)
                            const long maxFileSize = 5 * 1024 * 1024; // 5MB
                            if (imageFile.Length > maxFileSize)
                            {
                                await transaction.RollbackAsync();
                                ModelState.AddModelError("imageFile", "Ukuran file maksimal 5MB");
                                // Reload data untuk view
                                var machineForView = await _context.Machines
                                    .Include(m => m.Plant)
                                    .FirstOrDefaultAsync(m => m.Id == id);
                                if (machineForView != null)
                                {
                                    var plants = _context.Plants.OrderBy(p => p.Name).ToList();
                                    var plantSelectList = plants.Select(p => new { 
                                        Id = p.Id, 
                                        DisplayText = $"{p.Code} - {p.Name}" 
                                    }).ToList();
                                    ViewData["PlantId"] = new SelectList(plantSelectList, "Id", "DisplayText");
                                    return View(machineForView);
                                }
                            }
                            
                            // Jika ada image lama, hapus dulu
                            if (!string.IsNullOrEmpty(finalImageUrl))
                            {
                                try
                                {
                                    var oldImagePath = finalImageUrl.TrimStart('/');
                                    var oldFullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", oldImagePath);
                                    if (System.IO.File.Exists(oldFullPath))
                                    {
                                        System.IO.File.Delete(oldFullPath);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    System.Diagnostics.Debug.WriteLine($"Error deleting old image file: {ex.Message}");
                                }
                            }
                            
                            // Simpan file ke filesystem
                            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "machines");
                            Directory.CreateDirectory(uploadsFolder);
                            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(imageFile.FileName)}";
                            var filePath = Path.Combine(uploadsFolder, fileName);
                            
                            using (var stream = new FileStream(filePath, FileMode.Create))
                            {
                                await imageFile.CopyToAsync(stream);
                            }
                            
                            // Simpan path ke ImageUrl di database
                            finalImageUrl = $"/images/machines/{fileName}";
                        }
                        
                        // Detach existing machine dari context untuk menghindari tracking conflict
                        _context.Entry(existingMachine).State = EntityState.Detached;
                        
                        // Hapus machine lama
                        await _context.Database.ExecuteSqlRawAsync(
                            "DELETE FROM Machines WHERE Id = {0}", oldId);
                        
                        // Insert machine baru dengan ID baru menggunakan Entity Framework (lebih aman)
                        var newMachine = new Machine
                        {
                            Id = newId,
                            Name = machine.Name,
                            LineId = machine.LineId,
                            PlantId = machine.PlantId,
                            Status = machine.Status,
                            ManufacturingYear = machine.ManufacturingYear,
                            InstallationYear = machine.InstallationYear,
                            Description = machine.Description,
                            ImageUrl = string.IsNullOrEmpty(finalImageUrl) ? null : finalImageUrl
                        };
                        
                        // Add machine baru
                        _context.Machines.Add(newMachine);
                        await _context.SaveChangesAsync();
                        
                        // Commit transaction
                        await transaction.CommitAsync();
                        
                        // Sinkronkan gambar produk yang terhubung dengan mesin ini
                        await SyncProductsImageFromMachine(newMachine.Id);
                        
                    // ✅ PERBAIKAN: Broadcast SignalR untuk update status ke semua views
                    await _hubContext.Clients.All.SendAsync("OeeUpdated", new
                    {
                        Type = "MachineStatusUpdated",
                        MachineId = newMachine.Id,
                        MachineName = newMachine.Name,
                        MachineStatus = newMachine.Status.ToString(), // 'Aktif' atau 'TidakAktif'
                        Message = $"Status mesin {newMachine.Name} diubah menjadi {newMachine.Status}",
                        Timestamp = DateTime.Now,
                        RefreshOperatorData = true, // Flag untuk trigger refresh Operator View
                        RefreshDashboard = true, // Flag untuk trigger refresh Dashboard
                        RefreshOeeDetail = true // Flag untuk trigger refresh OEE Detail
                    });
                        
                        // Clear context tracking setelah commit berhasil
                        _context.ChangeTracker.Clear();
                        
                        // Setelah commit berhasil, langsung redirect tanpa reload
                        // IMPORTANT: Return langsung di sini, jangan sampai masuk ke bagian reload
                        return RedirectToAction(nameof(Machines));
                    }
                    catch (Exception ex)
                    {
                        // Rollback jika ada error
                        try
                        {
                            await transaction.RollbackAsync();
                        }
                        catch
                        {
                            // Ignore rollback error
                        }
                        
                        // Clear context tracking setelah rollback
                        _context.ChangeTracker.Clear();
                        
                        // Setelah rollback, machine dengan ID lama masih ada, jadi kita bisa reload dengan ID lama
                        // Jangan re-throw exception, biarkan masuk ke bagian reload view dengan error message
                        ModelState.AddModelError("", $"Error saat mengubah Machine ID: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"Error saat mengubah Machine ID: {ex.Message}");
                        System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                        if (ex.InnerException != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"InnerException: {ex.InnerException.Message}");
                        }
                    }
                }
                else
                {
                    // ID tidak diubah, update biasa
                    existingMachine.Name = machine.Name;
                    existingMachine.LineId = machine.LineId;
                    existingMachine.PlantId = machine.PlantId;
                    existingMachine.Status = machine.Status;
                    existingMachine.ManufacturingYear = machine.ManufacturingYear;
                    existingMachine.InstallationYear = machine.InstallationYear;
                    existingMachine.Description = machine.Description;

                    // ✅ PERBAIKAN: Handle delete image dari filesystem dan database
                    var deleteImage = Request.Form.ContainsKey("deleteImage") && Request.Form["deleteImage"].ToString() == "true";
                    if (deleteImage && !string.IsNullOrEmpty(existingMachine.ImageUrl))
                    {
                        // Hapus file dari filesystem
                        try
                        {
                            var imagePath = existingMachine.ImageUrl.TrimStart('/');
                            var fullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", imagePath);
                            if (System.IO.File.Exists(fullPath))
                            {
                                System.IO.File.Delete(fullPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error deleting image file: {ex.Message}");
                        }
                        
                        existingMachine.ImageUrl = null;
                    }

                    // ✅ PERBAIKAN: Handle image upload ke filesystem dan simpan path ke database
                    if (imageFile != null && imageFile.Length > 0)
                    {
                        // Validasi ukuran file (max 5MB)
                        const long maxFileSize = 5 * 1024 * 1024; // 5MB
                        if (imageFile.Length > maxFileSize)
                        {
                            ModelState.AddModelError("imageFile", "Ukuran file maksimal 5MB");
                            ReloadPlantsDropdown(machine.PlantId);
                            return View(machine);
                        }
                        
                        // Jika ada image lama, hapus dulu
                        if (!string.IsNullOrEmpty(existingMachine.ImageUrl))
                        {
                            try
                            {
                                var oldImagePath = existingMachine.ImageUrl.TrimStart('/');
                                var oldFullPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", oldImagePath);
                                if (System.IO.File.Exists(oldFullPath))
                                {
                                    System.IO.File.Delete(oldFullPath);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error deleting old image file: {ex.Message}");
                            }
                        }
                        
                        // Simpan file ke filesystem
                        var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "machines");
                        Directory.CreateDirectory(uploadsFolder);
                        var fileName = $"{Guid.NewGuid()}{Path.GetExtension(imageFile.FileName)}";
                        var filePath = Path.Combine(uploadsFolder, fileName);
                        
                        using (var stream = new FileStream(filePath, FileMode.Create))
                        {
                            await imageFile.CopyToAsync(stream);
                        }
                        
                        // Simpan path ke ImageUrl di database
                        existingMachine.ImageUrl = $"/images/machines/{fileName}";
                    }

                    _context.Update(existingMachine);
                    await _context.SaveChangesAsync();
                    
                    // Sinkronkan gambar produk yang terhubung dengan mesin ini
                    await SyncProductsImageFromMachine(existingMachine.Id);
                    
                    // ✅ PERBAIKAN: Broadcast SignalR untuk update status ke semua views
                    await _hubContext.Clients.All.SendAsync("OeeUpdated", new
                    {
                        Type = "MachineStatusUpdated",
                        MachineId = existingMachine.Id,
                        MachineName = existingMachine.Name,
                        MachineStatus = existingMachine.Status.ToString(), // 'Aktif' atau 'TidakAktif'
                        Message = $"Status mesin {existingMachine.Name} diubah menjadi {existingMachine.Status}",
                        Timestamp = DateTime.Now,
                        RefreshOperatorData = true, // Flag untuk trigger refresh Operator View
                        RefreshDashboard = true, // Flag untuk trigger refresh Dashboard
                        RefreshOeeDetail = true // Flag untuk trigger refresh OEE Detail
                    });
                    
                    // Setelah update berhasil, langsung redirect
                    // IMPORTANT: Return langsung di sini, jangan sampai masuk ke bagian reload
                    return RedirectToAction(nameof(Machines));
                }
            }
            catch (DbUpdateConcurrencyException ex)
            {
                ModelState.AddModelError("", "Data telah diubah oleh user lain. Silakan refresh dan coba lagi.");
            }
            catch (DbUpdateException ex)
            {
                var baseException = ex.GetBaseException();
                ModelState.AddModelError("", $"Terjadi error saat menyimpan ke database: {baseException.Message}");
                // Log error untuk debugging
                System.Diagnostics.Debug.WriteLine($"DbUpdateException: {baseException.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {baseException.StackTrace}");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Terjadi error: {ex.Message}");
                // Log error untuk debugging
                System.Diagnostics.Debug.WriteLine($"Exception: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    System.Diagnostics.Debug.WriteLine($"InnerException: {ex.InnerException.Message}");
                }
            }
        }
        
        // Jika ada error validation atau error saat save, reload semua data yang diperlukan untuk view
        // Selalu reload dengan ID lama (dari route parameter) karena jika transaction rollback, 
        // machine dengan ID lama masih ada di database
        try
        {
            // Reload dengan ID lama (dari route parameter) karena ini adalah ID yang pasti ada
            // jika transaction rollback atau jika belum ada perubahan ID
            var machineForView = await _context.Machines
                .Include(m => m.Plant)
                .FirstOrDefaultAsync(m => m.Id == id);
            
            // Jika tidak ditemukan dengan ID lama, coba dengan ID baru (untuk kasus edge case)
            if (machineForView == null && !string.IsNullOrEmpty(machine.Id) && id != machine.Id)
            {
                machineForView = await _context.Machines
                    .Include(m => m.Plant)
                    .FirstOrDefaultAsync(m => m.Id == machine.Id);
            }
            
            if (machineForView != null)
            {
                // Update machine object dengan data dari form, tapi keep data dari database untuk navigation properties
                // Jika machine ditemukan dengan ID lama, berarti transaction rollback atau ID belum diubah
                // Jadi kita tetap gunakan ID dari form (yang mungkin ID baru yang user input) untuk ditampilkan di form
                // Tapi kita perlu pastikan bahwa ID yang ditampilkan sesuai dengan yang user input
                if (machineForView.Id == id)
                {
                    // Machine ditemukan dengan ID lama, berarti rollback atau belum diubah
                    // Gunakan ID dari form (bisa ID baru yang user input) untuk ditampilkan di form
                    // Tapi kita perlu pastikan bahwa ID yang ditampilkan sesuai dengan yang user input
                    machineForView.Id = machine.Id;
                }
                // Jika machine ditemukan dengan ID baru, berarti ID sudah berhasil diubah sebelumnya
                // Tapi ini seharusnya tidak terjadi karena kita selalu cek ID lama dulu
                // Jika terjadi, tetap gunakan ID dari form
                else if (machineForView.Id != machine.Id)
                {
                    machineForView.Id = machine.Id;
                }
                
                machineForView.Name = machine.Name;
                machineForView.LineId = machine.LineId;
                machineForView.PlantId = machine.PlantId;
                machineForView.Status = machine.Status;
                machineForView.ManufacturingYear = machine.ManufacturingYear;
                machineForView.InstallationYear = machine.InstallationYear;
                machineForView.Description = machine.Description;
                // Keep existing ImageUrl dari database
                
                // Reload dropdown Plants
                var plants = await _context.Plants.OrderBy(p => p.Name).ToListAsync();
                var plantSelectList = plants.Select(p => new { 
                    Id = p.Id, 
                    DisplayText = $"{p.Code} - {p.Name}" 
                }).ToList();
                ViewData["PlantId"] = new SelectList(plantSelectList, "Id", "DisplayText", machine.PlantId);
                ViewBag.PlantId = ViewData["PlantId"];
                
                return View(machineForView);
            }
            else
            {
                // Jika machine tidak ditemukan dengan ID lama maupun baru, 
                // gunakan data dari form dan load Plant
                try
                {
                    if (machine.PlantId > 0)
                    {
                        machine.Plant = await _context.Plants.FindAsync(machine.PlantId);
                    }
                }
                catch
                {
                    // Ignore jika tidak bisa load Plant
                }
                
                // Reload dropdown Plants
                var plants = await _context.Plants.OrderBy(p => p.Name).ToListAsync();
                var plantSelectList = plants.Select(p => new { 
                    Id = p.Id, 
                    DisplayText = $"{p.Code} - {p.Name}" 
                }).ToList();
                ViewData["PlantId"] = new SelectList(plantSelectList, "Id", "DisplayText", machine.PlantId);
                ViewBag.PlantId = ViewData["PlantId"];
                
                return View(machine);
            }
        }
        catch (Exception ex)
        {
            // Jika error saat reload, set minimal data
            ModelState.AddModelError("", $"Error saat memuat data: {ex.Message}");
            
            // Log error untuk debugging
            System.Diagnostics.Debug.WriteLine($"Error saat reload machine: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            
            // Pastikan Plant di-load untuk machine object
            try
            {
                if (machine.Plant == null && machine.PlantId > 0)
                {
                    machine.Plant = await _context.Plants.FindAsync(machine.PlantId);
                }
            }
            catch
            {
                // Ignore jika tidak bisa load Plant
            }
        }
        
        // Fallback: set minimal data jika semua gagal
        try
        {
            var plantsFallback = await _context.Plants.OrderBy(p => p.Name).ToListAsync();
            var plantSelectListFallback = plantsFallback.Select(p => new { 
                Id = p.Id, 
                DisplayText = $"{p.Code} - {p.Name}" 
            }).ToList();
            ViewData["PlantId"] = new SelectList(plantSelectListFallback, "Id", "DisplayText", machine.PlantId);
            ViewBag.PlantId = ViewData["PlantId"];
            
            // Pastikan Plant di-load untuk machine object
            if (machine.Plant == null && machine.PlantId > 0)
            {
                try
                {
                    machine.Plant = await _context.Plants.FindAsync(machine.PlantId);
                }
                catch
                {
                    // Ignore jika tidak bisa load Plant
                }
            }
        }
        catch
        {
            // Jika masih error, set empty
            try
            {
                ViewData["PlantId"] = new SelectList(new List<object>(), "Id", "DisplayText");
                ViewBag.PlantId = ViewData["PlantId"];
            }
            catch
            {
                // Jika masih error, set minimal
                ViewBag.PlantId = new SelectList(new List<object>(), "Id", "DisplayText");
            }
        }
        
        // Pastikan machine object tidak null dan memiliki data minimal
        if (machine == null)
        {
            machine = new Machine { Id = id ?? "", PlantId = 0 };
        }
        
        // Pastikan ViewBag.PlantId selalu di-set
        if (ViewBag.PlantId == null)
        {
            ViewBag.PlantId = new SelectList(new List<object>(), "Id", "DisplayText");
        }
        
        return View(machine);
    }

    public async Task<IActionResult> DeleteMachine(string? id)
    {
        if (id == null) return NotFound();
        var machine = await _context.Machines.FindAsync(id);
        if (machine == null) return NotFound();
        return View(machine);
    }

    [HttpPost, ActionName("DeleteMachine")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteMachineConfirmed(string id)
    {
        var machine = await _context.Machines
            .Include(m => m.JobRuns)
            .Include(m => m.ProductMachines)
            .Include(m => m.MachineDowntimeReasons)
            .FirstOrDefaultAsync(m => m.Id == id);
        
        if (machine == null)
        {
            return RedirectToAction(nameof(Machines));
        }
        
        try
        {
            // Gunakan transaction untuk memastikan semua operasi berhasil atau rollback
            using var transaction = await _context.Database.BeginTransactionAsync();
            
            try
            {
                // 1. Hapus ProductionCounts yang terkait dengan JobRuns
                var jobRunIds = machine.JobRuns.Select(j => j.Id).ToList();
                if (jobRunIds.Any())
                {
                    var productionCounts = await _context.ProductionCounts
                        .Where(pc => jobRunIds.Contains(pc.JobRunId))
                        .ToListAsync();
                    if (productionCounts.Any())
                    {
                        _context.ProductionCounts.RemoveRange(productionCounts);
                    }
                    
                    // 2. Hapus DowntimeEvents yang terkait dengan JobRuns
                    var downtimeEvents = await _context.DowntimeEvents
                        .Where(de => jobRunIds.Contains(de.JobRunId))
                        .ToListAsync();
                    if (downtimeEvents.Any())
                    {
                        _context.DowntimeEvents.RemoveRange(downtimeEvents);
                    }
                }
                
                // 3. Hapus JobRuns
                if (machine.JobRuns.Any())
                {
                    _context.JobRuns.RemoveRange(machine.JobRuns);
                }
                
                // 4. Hapus ProductMachines
                if (machine.ProductMachines.Any())
                {
                    _context.ProductMachines.RemoveRange(machine.ProductMachines);
                }
                
                // 5. Hapus MachineDowntimeReasons
                if (machine.MachineDowntimeReasons.Any())
                {
                    _context.MachineDowntimeReasons.RemoveRange(machine.MachineDowntimeReasons);
                }
                
                // 6. Hapus Machine
                _context.Machines.Remove(machine);
                
                // Simpan semua perubahan
                await _context.SaveChangesAsync();
                
                // Commit transaction
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                // Rollback jika ada error
                try
                {
                    await transaction.RollbackAsync();
                }
                catch
                {
                    // Ignore rollback error
                }
                // Re-throw exception agar ditangkap oleh outer catch
                throw;
            }
        }
        catch (DbUpdateException ex)
        {
            // Log error untuk debugging
            System.Diagnostics.Debug.WriteLine($"Error saat menghapus machine: {ex.Message}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"InnerException: {ex.InnerException.Message}");
            }
            // Redirect kembali ke halaman delete dengan error message
            TempData["ErrorMessage"] = "Tidak dapat menghapus machine. Pastikan tidak ada data terkait yang masih digunakan.";
            return RedirectToAction(nameof(DeleteMachine), new { id });
        }
        catch (Exception ex)
        {
            // Log error untuk debugging
            System.Diagnostics.Debug.WriteLine($"Error saat menghapus machine: {ex.Message}");
            if (ex.InnerException != null)
            {
                System.Diagnostics.Debug.WriteLine($"InnerException: {ex.InnerException.Message}");
            }
            // Redirect kembali ke halaman delete dengan error message
            TempData["ErrorMessage"] = $"Error saat menghapus machine: {ex.Message}";
            return RedirectToAction(nameof(DeleteMachine), new { id });
        }
        
        return RedirectToAction(nameof(Machines));
    }

    private bool MachineExists(string id) => _context.Machines.Any(e => e.Id == id);

    // ========== PRODUCTS CRUD ==========
    public async Task<IActionResult> Products()
    {
        var products = await _context.Products
            .Include(p => p.Plant)
            .ToListAsync();
        return View(products);
    }

    // Download Template Excel
    [HttpGet]
    public IActionResult DownloadProductTemplate()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Products Template");
        
        // Header
        worksheet.Cell(1, 1).Value = "MaterialCode";
        worksheet.Cell(1, 2).Value = "Name";
        worksheet.Cell(1, 3).Value = "UoM";
        worksheet.Cell(1, 4).Value = "SLOC";
        worksheet.Cell(1, 5).Value = "PlantCode";
        worksheet.Cell(1, 6).Value = "StandarCycleTime";
        worksheet.Cell(1, 7).Value = "ImageUrl";
        
        // Style header
        var headerRange = worksheet.Range(1, 1, 1, 7);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        
        // Auto fit columns
        worksheet.Columns().AdjustToContents();
        
        // Add sample data (row 2)
        worksheet.Cell(2, 1).Value = "MAT001";
        worksheet.Cell(2, 2).Value = "Product Sample";
        worksheet.Cell(2, 3).Value = "PCS";
        worksheet.Cell(2, 4).Value = "WH01";
        worksheet.Cell(2, 5).Value = "P1000"; // Harus sesuai dengan Plant Code di database
        worksheet.Cell(2, 6).Value = 30.5;
        worksheet.Cell(2, 7).Value = ""; // Optional
        
        // Add instruction sheet
        var instructionSheet = workbook.Worksheets.Add("Instructions");
        instructionSheet.Cell(1, 1).Value = "PETUNJUK PENGGUNAAN TEMPLATE";
        instructionSheet.Cell(1, 1).Style.Font.Bold = true;
        instructionSheet.Cell(1, 1).Style.Font.FontSize = 14;
        
        instructionSheet.Cell(3, 1).Value = "1. MaterialCode: Kode material product (wajib, unique)";
        instructionSheet.Cell(4, 1).Value = "2. Name: Nama product (wajib)";
        instructionSheet.Cell(5, 1).Value = "3. UoM: Unit of Measure - satuan (wajib, contoh: PCS, KG, M)";
        instructionSheet.Cell(6, 1).Value = "4. SLOC: Storage Location - lokasi penyimpanan (wajib)";
        instructionSheet.Cell(7, 1).Value = "5. PlantCode: Kode Plant (wajib, harus sesuai dengan Plant Code di database)";
        instructionSheet.Cell(8, 1).Value = "6. StandarCycleTime: Waktu cycle ideal dalam detik (wajib, angka)";
        instructionSheet.Cell(9, 1).Value = "7. ImageUrl: URL gambar product (optional)";
        
        instructionSheet.Cell(11, 1).Value = "CATATAN PENTING:";
        instructionSheet.Cell(11, 1).Style.Font.Bold = true;
        instructionSheet.Cell(12, 1).Value = "- PlantCode harus sesuai dengan kode Plant yang ada di database";
        instructionSheet.Cell(13, 1).Value = "- Jika MaterialCode sudah ada, data akan di-update";
        instructionSheet.Cell(14, 1).Value = "- Jika MaterialCode belum ada, data akan ditambahkan sebagai product baru";
        
        instructionSheet.Columns().AdjustToContents();
        
        // Save to stream
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();
        
        var fileName = $"ProductTemplate_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // Upload & Process Excel
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadProductExcel(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            TempData["UploadError"] = "File tidak boleh kosong.";
            return RedirectToAction(nameof(Products));
        }
        
        if (!Path.GetExtension(excelFile.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(excelFile.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
        {
            TempData["UploadError"] = "Format file harus .xlsx atau .xls";
            return RedirectToAction(nameof(Products));
        }
        
        try
        {
            int addedCount = 0;
            int updatedCount = 0;
            int errorCount = 0;
            var errors = new List<string>();
            
            using var stream = new MemoryStream();
            await excelFile.CopyToAsync(stream);
            
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet("Products Template");
            
            if (worksheet == null)
            {
                TempData["UploadError"] = "Sheet 'Products Template' tidak ditemukan. Pastikan menggunakan template yang benar.";
                return RedirectToAction(nameof(Products));
            }
            
            // Get all plants for lookup
            var plants = await _context.Plants.ToDictionaryAsync(p => p.Code, p => p.Id);
            
            // Process rows (skip header)
            var rows = worksheet.RowsUsed().Skip(1);
            
            foreach (var row in rows)
            {
                try
                {
                    var materialCode = row.Cell(1).GetString().Trim();
                    var name = row.Cell(2).GetString().Trim();
                    var uom = row.Cell(3).GetString().Trim();
                    var sloc = row.Cell(4).GetString().Trim();
                    var plantCode = row.Cell(5).GetString().Trim();
                    var cycleTimeStr = row.Cell(6).GetString().Trim();
                    var imageUrl = row.Cell(7).GetString().Trim();
                    
                    // Validasi required fields
                    if (string.IsNullOrEmpty(materialCode) || string.IsNullOrEmpty(name) || 
                        string.IsNullOrEmpty(plantCode))
                    {
                        errors.Add($"Row {row.RowNumber()}: MaterialCode, Name, dan PlantCode wajib diisi.");
                        errorCount++;
                        continue;
                    }
                    
                    // Validasi PlantCode
                    if (!plants.ContainsKey(plantCode))
                    {
                        errors.Add($"Row {row.RowNumber()}: Plant Code '{plantCode}' tidak ditemukan.");
                        errorCount++;
                        continue;
                    }
                    
                    // Parse cycle time
                    if (!double.TryParse(cycleTimeStr, out double cycleTime))
                    {
                        errors.Add($"Row {row.RowNumber()}: StandarCycleTime harus berupa angka.");
                        errorCount++;
                        continue;
                    }
                    
                    // Check if product exists
                    var existingProduct = await _context.Products
                        .FirstOrDefaultAsync(p => p.MaterialCode == materialCode);
                    
                    if (existingProduct != null)
                    {
                        // Update existing
                        existingProduct.Name = name;
                        existingProduct.UoM = uom;
                        existingProduct.SLOC = sloc;
                        existingProduct.PlantId = plants[plantCode];
                        existingProduct.StandarCycleTime = cycleTime;
                        if (!string.IsNullOrEmpty(imageUrl))
                        {
                            existingProduct.ImageUrl = imageUrl;
                        }
                        updatedCount++;
                    }
                    else
                    {
                        // Add new
                        var newProduct = new Product
                        {
                            MaterialCode = materialCode,
                            Name = name,
                            UoM = uom,
                            SLOC = sloc,
                            PlantId = plants[plantCode],
                            StandarCycleTime = cycleTime,
                            ImageUrl = string.IsNullOrEmpty(imageUrl) ? null : imageUrl
                        };
                        _context.Products.Add(newProduct);
                        addedCount++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Row {row.RowNumber()}: {ex.Message}");
                    errorCount++;
                }
            }
            
            await _context.SaveChangesAsync();
            
            var successMessage = $"Upload berhasil! Ditambahkan: {addedCount}, Diupdate: {updatedCount}";
            if (errorCount > 0)
            {
                successMessage += $", Error: {errorCount}";
            }
            
            TempData["UploadSuccess"] = successMessage;
            
            if (errors.Any())
            {
                TempData["UploadError"] = "Detail Error:<br/>" + string.Join("<br/>", errors.Take(10));
                if (errors.Count > 10)
                {
                    TempData["UploadError"] += $"<br/>... dan {errors.Count - 10} error lainnya.";
                }
            }
        }
        catch (Exception ex)
        {
            TempData["UploadError"] = $"Terjadi error: {ex.Message}";
        }
        
        return RedirectToAction(nameof(Products));
    }

    public IActionResult CreateProduct()
    {
        // Ambil semua plants dari database untuk dropdown, diurutkan berdasarkan Name
        var plants = _context.Plants.OrderBy(p => p.Name).ToList();
        // Buat SelectList dengan menampilkan Code - Name
        var plantSelectList = plants.Select(p => new { 
            Id = p.Id, 
            DisplayText = $"{p.Code} - {p.Name}" 
        }).ToList();
        ViewData["PlantId"] = new SelectList(plantSelectList, "Id", "DisplayText");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProduct(Product product)
    {
        if (ModelState.IsValid)
        {
            _context.Add(product);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Products));
        }
        // Jika ada error validation, reload dropdown Plants dari database
        var plants = _context.Plants.OrderBy(p => p.Name).ToList();
        var plantSelectList = plants.Select(p => new { 
            Id = p.Id, 
            DisplayText = $"{p.Code} - {p.Name}" 
        }).ToList();
        ViewData["PlantId"] = new SelectList(plantSelectList, "Id", "DisplayText", product.PlantId);
        return View(product);
    }

    public async Task<IActionResult> EditProduct(int? id)
    {
        if (id == null) return NotFound();
        var product = await _context.Products
            .Include(p => p.Plant)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();
        
        // Ambil semua plants dari database untuk dropdown, diurutkan berdasarkan Name
        var plants = await _context.Plants.OrderBy(p => p.Name).ToListAsync();
        var plantSelectList = plants.Select(p => new { 
            Id = p.Id, 
            DisplayText = $"{p.Code} - {p.Name}" 
        }).ToList();
        ViewData["PlantId"] = new SelectList(plantSelectList, "Id", "DisplayText", product.PlantId);
        
        return View(product);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProduct(int id, Product product)
    {
        if (id != product.Id) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(product);
                await _context.SaveChangesAsync();
                
                // Sinkronkan gambar produk dengan gambar mesin yang terhubung
                await SyncProductImageFromMachines(product.Id);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProductExists(product.Id)) return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Products));
        }
        
        // Jika ada error validation, reload dropdown Plants dari database
        var plants = await _context.Plants.OrderBy(p => p.Name).ToListAsync();
        var plantSelectList = plants.Select(p => new { 
            Id = p.Id, 
            DisplayText = $"{p.Code} - {p.Name}" 
        }).ToList();
        ViewData["PlantId"] = new SelectList(plantSelectList, "Id", "DisplayText", product.PlantId);
        
        return View(product);
    }

    public async Task<IActionResult> DeleteProduct(int? id)
    {
        if (id == null) return NotFound();
        var product = await _context.Products.FindAsync(id);
        if (product == null) return NotFound();
        return View(product);
    }

    [HttpPost, ActionName("DeleteProduct")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProductConfirmed(int id)
    {
        var product = await _context.Products
            .Include(p => p.ProductNgTypes)
            .Include(p => p.ProductMachines)
            .Include(p => p.WorkOrders)
            .FirstOrDefaultAsync(p => p.Id == id);
        
        if (product != null)
        {
            // ✅ PERBAIKAN: Hapus semua relasi terlebih dahulu sebelum menghapus Product
            
            // 1. Hapus ProductNgTypes (many-to-many dengan NgType)
            if (product.ProductNgTypes.Any())
            {
                _context.ProductNgTypes.RemoveRange(product.ProductNgTypes);
            }
            
            // 2. Hapus ProductMachines (many-to-many dengan Machine)
            if (product.ProductMachines.Any())
            {
                _context.ProductMachines.RemoveRange(product.ProductMachines);
            }
            
            // 3. Cek apakah ada WorkOrders yang menggunakan Product ini
            if (product.WorkOrders.Any())
            {
                TempData["DeleteError"] = $"Tidak dapat menghapus Product '{product.Name}' karena masih digunakan oleh {product.WorkOrders.Count} Work Order(s). Hapus Work Order terlebih dahulu.";
                return RedirectToAction(nameof(Products));
            }
            
            // 4. Hapus Product setelah semua relasi dihapus
            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Products));
    }

    private bool ProductExists(int id) => _context.Products.Any(e => e.Id == id);

    // ========== PLANTS CRUD ==========
    public async Task<IActionResult> Plants()
    {
        // Ambil semua plants dari database, diurutkan berdasarkan ID
        var plants = await _context.Plants
            .OrderBy(p => p.Id)
            .ToListAsync();
        return View(plants);
    }

    public IActionResult CreatePlant()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePlant(Plant plant)
    {
        if (ModelState.IsValid)
        {
            _context.Plants.Add(plant);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Plants));
        }
        return View(plant);
    }

    public async Task<IActionResult> EditPlant(int? id)
    {
        if (id == null) return NotFound();
        var plant = await _context.Plants.FindAsync(id);
        if (plant == null) return NotFound();
        return View(plant);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPlant(int id, Plant plant)
    {
        if (id != plant.Id) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                _context.Plants.Update(plant);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.Plants.AnyAsync(p => p.Id == plant.Id)) return NotFound();
                throw;
            }
            return RedirectToAction(nameof(Plants));
        }
        return View(plant);
    }

    public async Task<IActionResult> DeletePlant(int? id)
    {
        if (id == null) return NotFound();
        var plant = await _context.Plants.FindAsync(id);
        if (plant == null) return NotFound();
        return View(plant);
    }

    [HttpPost, ActionName("DeletePlant")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeletePlantConfirmed(int id)
    {
        var plant = await _context.Plants.FindAsync(id);
        if (plant != null)
        {
            _context.Plants.Remove(plant);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Plants));
    }

    // ========== PRODUCT-MACHINE MAPPING ==========
    public async Task<IActionResult> ProductMachineMappings()
    {
        var data = await _context.Products
            .Include(p => p.ProductMachines)
                .ThenInclude(pm => pm.Machine)
            .ToListAsync();
        return View(data);
    }

    public async Task<IActionResult> EditProductMachines(int? id)
    {
        if (id == null) return NotFound();
        var product = await _context.Products
            .Include(p => p.ProductMachines)
            .FirstOrDefaultAsync(p => p.Id == id.Value);
        if (product == null) return NotFound();

        var allMachines = await _context.Machines.ToListAsync();
        var selectedMachineIds = product.ProductMachines.Select(pm => pm.MachineId).ToHashSet();

        ViewBag.Product = product;
        ViewBag.Machines = allMachines;
        ViewBag.SelectedMachineIds = selectedMachineIds;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProductMachines(int id, string[] machineIds)
    {
        var product = await _context.Products
            .Include(p => p.ProductMachines)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();

        // Hapus mapping lama
        _context.ProductMachines.RemoveRange(product.ProductMachines);

        // Tambah mapping baru
        foreach (var mid in machineIds.Distinct())
        {
            _context.ProductMachines.Add(new ProductMachine
            {
                ProductId = product.Id,
                MachineId = mid
            });
        }

        await _context.SaveChangesAsync();
        
        // Sinkronkan gambar produk dengan gambar mesin yang terhubung
        await SyncProductImageFromMachines(id);
        
        TempData["SuccessMessage"] = "Mapping Product ⇄ Machine berhasil disimpan.";
        return RedirectToAction(nameof(ProductMachineMappings));
    }
    
    // Helper method untuk sinkronkan gambar produk dari mesin yang terhubung
    private async Task SyncProductImageFromMachines(int productId)
    {
        var product = await _context.Products
            .Include(p => p.ProductMachines)
                .ThenInclude(pm => pm.Machine)
            .FirstOrDefaultAsync(p => p.Id == productId);
        
        if (product == null) return;
        
        // Ambil gambar dari mesin pertama yang memiliki gambar
        var machineWithImage = product.ProductMachines
            .Select(pm => pm.Machine)
            .FirstOrDefault(m => m != null && !string.IsNullOrEmpty(m.ImageUrl));
        
        if (machineWithImage != null && !string.IsNullOrEmpty(machineWithImage.ImageUrl))
        {
            product.ImageUrl = machineWithImage.ImageUrl;
            await _context.SaveChangesAsync();
        }
    }
    
    // Helper method untuk sinkronkan semua produk yang terhubung dengan mesin tertentu
    private async Task SyncProductsImageFromMachine(string machineId)
    {
        var products = await _context.ProductMachines
            .Where(pm => pm.MachineId == machineId)
            .Select(pm => pm.ProductId)
            .Distinct()
            .ToListAsync();
        
        foreach (var productId in products)
        {
            await SyncProductImageFromMachines(productId);
        }
    }
    
    // Action untuk sinkronkan semua gambar produk dari mesin yang terhubung
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SyncAllProductImages()
    {
        try
        {
            var products = await _context.Products
                .Include(p => p.ProductMachines)
                    .ThenInclude(pm => pm.Machine)
                .Where(p => p.ProductMachines.Any())
                .ToListAsync();
            
            int syncedCount = 0;
            foreach (var product in products)
            {
                // Ambil gambar dari mesin pertama yang memiliki gambar
                var machineWithImage = product.ProductMachines
                    .Select(pm => pm.Machine)
                    .FirstOrDefault(m => m != null && !string.IsNullOrEmpty(m.ImageUrl));
                
                if (machineWithImage != null && !string.IsNullOrEmpty(machineWithImage.ImageUrl))
                {
                    if (product.ImageUrl != machineWithImage.ImageUrl)
                    {
                        product.ImageUrl = machineWithImage.ImageUrl;
                        syncedCount++;
                    }
                }
            }
            
            if (syncedCount > 0)
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"Berhasil menyinkronkan {syncedCount} gambar produk dengan gambar mesin.";
            }
            else
            {
                TempData["InfoMessage"] = "Tidak ada produk yang perlu disinkronkan. Semua produk sudah menggunakan gambar mesin yang terhubung.";
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error saat sinkronisasi: {ex.Message}";
        }
        
        return RedirectToAction(nameof(Products));
    }

    // ========== PRODUCT-NG MAPPING ==========
    public async Task<IActionResult> ProductNgMappings()
    {
        var data = await _context.Products
            .Include(p => p.ProductNgTypes)
                .ThenInclude(pn => pn.NgType)
            .ToListAsync();
        return View(data);
    }

    public async Task<IActionResult> EditProductNgTypes(int? id)
    {
        if (id == null) return NotFound();
        var product = await _context.Products
            .Include(p => p.ProductNgTypes)
            .FirstOrDefaultAsync(p => p.Id == id.Value);
        if (product == null) return NotFound();

        var allNgTypes = await _context.NgTypes.OrderBy(n => n.Code).ToListAsync();
        var selectedNgTypeIds = product.ProductNgTypes.Select(pn => pn.NgTypeId).ToHashSet();

        ViewBag.Product = product;
        ViewBag.NgTypes = allNgTypes;
        ViewBag.SelectedNgTypeIds = selectedNgTypeIds;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditProductNgTypes(int id, int[] ngTypeIds)
    {
        var product = await _context.Products
            .Include(p => p.ProductNgTypes)
            .FirstOrDefaultAsync(p => p.Id == id);
        if (product == null) return NotFound();

        // Hapus mapping lama
        _context.ProductNgTypes.RemoveRange(product.ProductNgTypes);

        // Tambah mapping baru
        foreach (var ngId in ngTypeIds.Distinct())
        {
            _context.ProductNgTypes.Add(new ProductNgType
            {
                ProductId = product.Id,
                NgTypeId = ngId
            });
        }

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = "Mapping Product ⇄ NG Type berhasil disimpan.";
        return RedirectToAction(nameof(ProductNgMappings));
    }

    // ========== DOWNTIME-MACHINE MAPPING ==========
    public async Task<IActionResult> DowntimeMachineMappings()
    {
        // Ubah untuk menampilkan mesin, bukan downtime reasons
        var machines = await _context.Machines
            .Include(m => m.MachineDowntimeReasons)
                .ThenInclude(md => md.DowntimeReason)
            .Include(m => m.Plant)
            .OrderBy(m => m.Name)
            .ToListAsync();
        return View(machines);
    }

    public async Task<IActionResult> EditDowntimeMachines(string? id)
    {
        if (id == null) return NotFound();
        // Ubah untuk mengambil mesin, bukan reason
        var machine = await _context.Machines
            .Include(m => m.MachineDowntimeReasons)
            .Include(m => m.Plant)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (machine == null) return NotFound();

        var allReasons = await _context.DowntimeReasons
            .OrderBy(r => r.Category)
            .ThenBy(r => r.Description)
            .ToListAsync();
        var selectedReasonIds = machine.MachineDowntimeReasons.Select(md => md.DowntimeReasonId).ToHashSet();

        ViewBag.Machine = machine;
        ViewBag.Reasons = allReasons;
        ViewBag.SelectedReasonIds = selectedReasonIds;

        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDowntimeMachines(string id, int[] reasonIds)
    {
        // Ubah parameter dari machineIds menjadi reasonIds
        var machine = await _context.Machines
            .Include(m => m.MachineDowntimeReasons)
            .FirstOrDefaultAsync(m => m.Id == id);
        if (machine == null) return NotFound();

        // Hapus mapping lama untuk mesin ini
        _context.MachineDowntimeReasons.RemoveRange(machine.MachineDowntimeReasons);

        // Tambah mapping baru (distinct)
        foreach (var rid in (reasonIds ?? Array.Empty<int>()).Distinct())
        {
            _context.MachineDowntimeReasons.Add(new MachineDowntimeReason
            {
                MachineId = machine.Id,
                DowntimeReasonId = rid
            });
        }

        await _context.SaveChangesAsync();
        TempData["SuccessMessage"] = $"Mapping Downtime Reasons untuk mesin {machine.Name} berhasil disimpan.";
        return RedirectToAction(nameof(DowntimeMachineMappings));
    }

    // ========== WORK ORDERS CRUD ==========
    public async Task<IActionResult> WorkOrders()
    {
        var workOrders = await _context.WorkOrders
            .Include(w => w.Product)
            .Include(w => w.Shift) // Include Shift untuk menampilkan nama shift
            .OrderBy(w => w.PlannedDate ?? DateTime.MaxValue) // Urutkan berdasarkan tanggal dari lama ke baru (null di akhir)
            .ThenBy(w => w.Id) // Jika tanggal sama, urutkan berdasarkan ID
            .ToListAsync();
        return View(workOrders);
    }

    public async Task<IActionResult> CreateWorkOrder()
    {
        var vm = new CreateWorkOrderViewModel
        {
            Plants = await _context.Plants
                .OrderBy(p => p.Name)
                .ToListAsync(),
            Machines = await _context.Machines
                .Include(m => m.Plant)
                .OrderBy(m => m.Name)
                .ToListAsync(),
            Products = await _context.Products.ToListAsync(),
            Shifts = await _context.Shifts.ToListAsync(),
            Operators = await _context.Users.Where(u => u.Role == UserRole.Operator).ToListAsync(),
            ManPowers = await GetManPowersSafe()
        };
        
        // Initialize dengan 1 schedule kosong
        if (vm.Schedules.Count == 0)
        {
            vm.Schedules.Add(new WorkOrderScheduleItem());
        }
        
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateWorkOrder(CreateWorkOrderViewModel vm)
    {
        // Validasi plant harus dipilih
        if (!vm.PlantId.HasValue || vm.PlantId.Value <= 0)
        {
            ModelState.AddModelError(nameof(vm.PlantId), "Plant harus dipilih.");
        }
        
        // Validasi mesin harus dipilih
        if (string.IsNullOrEmpty(vm.MachineId))
        {
            ModelState.AddModelError(nameof(vm.MachineId), "Mesin harus dipilih.");
        }
        
        // Validasi minimal 1 schedule
        if (vm.Schedules == null || vm.Schedules.Count == 0)
        {
            ModelState.AddModelError("", "Minimal harus ada 1 jadwal.");
        }
        
        // Validasi sinkronisasi Plant, Machine, dan Product
        if (vm.PlantId.HasValue && !string.IsNullOrEmpty(vm.MachineId))
        {
            // Cek apakah machine dari plant yang dipilih
            var machine = await _context.Machines
                .FirstOrDefaultAsync(m => m.Id == vm.MachineId);
            
            if (machine != null && machine.PlantId != vm.PlantId.Value)
            {
                ModelState.AddModelError(nameof(vm.MachineId), 
                    "Mesin yang dipilih tidak berasal dari Plant yang dipilih.");
            }
        }
        
        // Validasi setiap schedule
        if (vm.Schedules != null && !string.IsNullOrEmpty(vm.MachineId) && vm.PlantId.HasValue)
        {
            for (int i = 0; i < vm.Schedules.Count; i++)
            {
                var schedule = vm.Schedules[i];
                
                // Validasi product harus dipilih
                if (schedule.ProductId <= 0)
                {
                    ModelState.AddModelError($"Schedules[{i}].ProductId", "Product harus dipilih.");
                }
                
                // Validasi product-plant compatibility (sinkronisasi)
                if (schedule.ProductId > 0)
                {
                    var product = await _context.Products
                        .FirstOrDefaultAsync(p => p.Id == schedule.ProductId);
                    
                    if (product != null)
                    {
                        // Validasi: Product harus dari Plant yang sama dengan yang dipilih
                        if (product.PlantId != vm.PlantId.Value)
                        {
                            ModelState.AddModelError($"Schedules[{i}].ProductId", 
                                "Produk ini tidak berasal dari Plant yang dipilih.");
                        }
                        // Validasi: Product harus bisa dijalankan di machine yang dipilih
                        // Mapping akan dibuat otomatis di transaction jika belum ada
                    }
                    else
                    {
                        ModelState.AddModelError($"Schedules[{i}].ProductId", 
                            "Product tidak ditemukan.");
                    }
                }
                
                // Validasi shift harus dipilih
                if (schedule.ShiftId <= 0)
                {
                    ModelState.AddModelError($"Schedules[{i}].ShiftId", "Shift harus dipilih.");
                }
                
                // Validasi qty
                if (schedule.TargetQuantity <= 0)
                {
                    ModelState.AddModelError($"Schedules[{i}].TargetQuantity", 
                        "Quantity harus lebih dari 0.");
                }
            }
        }
        
        if (ModelState.IsValid && !string.IsNullOrEmpty(vm.MachineId) && vm.PlantId.HasValue && vm.Schedules != null && vm.Schedules.Count > 0)
        {
            var createdWorkOrders = new List<string>();
            
            // Gunakan transaction untuk memastikan semua data konsisten
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Step 1: Sinkronkan ProductMachine mappings terlebih dahulu
                var productMachineMappings = new List<ProductMachine>();
                foreach (var schedule in vm.Schedules)
                {
                    if (schedule.ProductId > 0 && !string.IsNullOrEmpty(vm.MachineId))
                    {
                        // Cek apakah mapping sudah ada
                        var existingMapping = await _context.ProductMachines
                            .FirstOrDefaultAsync(pm => pm.ProductId == schedule.ProductId && pm.MachineId == vm.MachineId);
                        
                        if (existingMapping == null)
                        {
                            // Buat mapping baru untuk sinkronisasi
                            var newMapping = new ProductMachine
                            {
                                ProductId = schedule.ProductId,
                                MachineId = vm.MachineId
                            };
                            productMachineMappings.Add(newMapping);
                        }
                    }
                }
                
                // Simpan semua ProductMachine mappings sekaligus
                if (productMachineMappings.Count > 0)
                {
                    await _context.ProductMachines.AddRangeAsync(productMachineMappings);
                    await _context.SaveChangesAsync();
                }
                
                // Step 2: Load shift data sekali untuk semua schedules
                var shiftIds = vm.Schedules.Select(s => s.ShiftId).Distinct().ToList();
                var shifts = await _context.Shifts
                    .Where(s => shiftIds.Contains(s.Id))
                    .ToDictionaryAsync(s => s.Id);
                
                // Step 3: Create WorkOrders dan JobRuns
                foreach (var schedule in vm.Schedules)
                {
                    // Create WorkOrder untuk setiap schedule
                    var workOrder = new WorkOrder
                    {
                        ProductId = schedule.ProductId,
                        TargetQuantity = schedule.TargetQuantity,
                        Status = WorkOrderStatus.InProgress, // Langsung InProgress karena akan di-assign
                        PlannedDate = schedule.PlannedDate, // Simpan planned date
                        ShiftId = schedule.ShiftId // Simpan shift ID
                    };
                    
                    _context.WorkOrders.Add(workOrder);
                    await _context.SaveChangesAsync(); // Save untuk dapat ID
                    
                    // Generate nomor unik Work Order
                    workOrder.OrderNumber = $"WO-{DateTime.Now:yyyyMMdd}-{workOrder.Id:D4}";
                    _context.WorkOrders.Update(workOrder); // Update OrderNumber
                    
                    // Hitung StartTime berdasarkan PlannedDate dan Shift
                    DateTime startTime = schedule.PlannedDate.Date;
                    
                    if (shifts.TryGetValue(schedule.ShiftId, out var shift))
                    {
                        startTime = schedule.PlannedDate.Date + shift.StartTime;
                        
                        // Handle shift malam (end < start)
                        if (shift.EndTime < shift.StartTime)
                        {
                            // Shift malam, tidak perlu ubah
                        }
                    }
                    
                    // Create JobRun untuk planning
                    var jobRun = new JobRun
                    {
                        WorkOrderId = workOrder.Id,
                        MachineId = vm.MachineId!,
                        OperatorId = schedule.OperatorId ?? 1,
                        ManPowerId = schedule.ManPowerId,
                        StartTime = startTime,
                        EndTime = null
                    };
                    
                    _context.JobRuns.Add(jobRun);
                    createdWorkOrders.Add(workOrder.OrderNumber);
                }
                
                // Save semua perubahan sekaligus (OrderNumber updates dan JobRuns)
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                
                var machineName = await _context.Machines
                    .Where(m => m.Id == vm.MachineId)
                    .Select(m => m.Name)
                    .FirstOrDefaultAsync();
                
                TempData["SuccessMessage"] = $"Berhasil membuat {createdWorkOrders.Count} Work Order untuk mesin {machineName}: {string.Join(", ", createdWorkOrders)}";
                return RedirectToAction(nameof(WorkOrders));
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                ModelState.AddModelError("", $"Terjadi error saat membuat Work Order: {ex.Message}");
                // Fall through to reload dropdowns
            }
        }
        
        // Reload dropdowns jika ada error atau validation failed
        vm.Plants = await _context.Plants
            .OrderBy(p => p.Name)
            .ToListAsync();
        vm.Machines = await _context.Machines
            .Include(m => m.Plant)
            .OrderBy(m => m.Name)
            .ToListAsync();
        vm.Products = await _context.Products.ToListAsync();
        vm.Shifts = await _context.Shifts.ToListAsync();
        vm.Operators = await _context.Users.Where(u => u.Role == UserRole.Operator).ToListAsync();
        
        // Ensure at least 1 schedule jika tidak ada schedule atau semua schedule kosong
        if (vm.Schedules == null || vm.Schedules.Count == 0)
        {
            vm.Schedules = new List<WorkOrderScheduleItem> { new WorkOrderScheduleItem() };
        }
        
        // Log validation errors untuk debugging
        if (!ModelState.IsValid)
        {
            var errors = ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .SelectMany(x => x.Value.Errors.Select(e => $"{x.Key}: {e.ErrorMessage}"));
            
            System.Diagnostics.Debug.WriteLine("Validation Errors:");
            foreach (var error in errors)
            {
                System.Diagnostics.Debug.WriteLine($"  - {error}");
            }
        }
        
        return View(vm);
    }

    [HttpGet]
    public async Task<IActionResult> GetMachinesForProduct(int productId)
    {
        var machines = await _context.ProductMachines
            .Where(pm => pm.ProductId == productId)
            .Select(pm => new
            {
                pm.MachineId,
                pm.Machine!.Name,
                pm.Machine.LineId
            })
            .ToListAsync();

        return Json(machines);
    }
    
    [HttpGet]
    public async Task<IActionResult> GetProductsForMachine(string machineId)
    {
        // Pastikan data selalu fresh dari database (no cache)
        var products = await _context.ProductMachines
            .AsNoTracking() // Pastikan tidak ada tracking/cache
            .Where(pm => pm.MachineId == machineId)
            .Include(pm => pm.Product) // Pastikan Product di-load
            .Select(pm => new
            {
                productId = pm.ProductId,
                name = pm.Product != null ? pm.Product.Name : "",
                materialCode = pm.Product != null ? pm.Product.MaterialCode : ""
            })
            .ToListAsync();
        
        return Json(products);
    }
    
    public async Task<IActionResult> GetProductsByPlant(int plantId)
    {
        // Pastikan data selalu fresh dari database (no cache)
        var products = await _context.Products
            .AsNoTracking() // Pastikan tidak ada tracking/cache
            .Where(p => p.PlantId == plantId)
            .Select(p => new
            {
                productId = p.Id,
                name = p.Name,
                materialCode = p.MaterialCode
            })
            .OrderBy(p => p.name)
            .ToListAsync();
        
        return Json(products);
    }

    public async Task<IActionResult> EditWorkOrder(int? id)
    {
        if (id == null) return NotFound();
        var workOrder = await _context.WorkOrders
            .Include(w => w.Shift)
            .FirstOrDefaultAsync(w => w.Id == id);
        if (workOrder == null) return NotFound();
        ViewData["ProductId"] = new SelectList(await _context.Products.ToListAsync(), "Id", "Name", workOrder.ProductId);
        ViewData["ShiftId"] = new SelectList(await _context.Shifts.ToListAsync(), "Id", "Name", workOrder.ShiftId);
        return View(workOrder);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditWorkOrder(int id, WorkOrder workOrder)
    {
        if (id != workOrder.Id) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                // Load existing work order untuk update
                var existingWorkOrder = await _context.WorkOrders.FindAsync(id);
                if (existingWorkOrder == null) return NotFound();
                
                // Update fields
                existingWorkOrder.OrderNumber = workOrder.OrderNumber;
                existingWorkOrder.ProductId = workOrder.ProductId;
                existingWorkOrder.TargetQuantity = workOrder.TargetQuantity;
                existingWorkOrder.Status = workOrder.Status;
                existingWorkOrder.PlannedDate = workOrder.PlannedDate;
                existingWorkOrder.ShiftId = workOrder.ShiftId;
                
                _context.Update(existingWorkOrder);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!WorkOrderExists(workOrder.Id)) return NotFound();
                throw;
            }
            return RedirectToAction(nameof(WorkOrders));
        }
        ViewData["ProductId"] = new SelectList(await _context.Products.ToListAsync(), "Id", "Name", workOrder.ProductId);
        ViewData["ShiftId"] = new SelectList(await _context.Shifts.ToListAsync(), "Id", "Name", workOrder.ShiftId);
        return View(workOrder);
    }

    public async Task<IActionResult> DeleteWorkOrder(int? id)
    {
        if (id == null) return NotFound();
        var workOrder = await _context.WorkOrders
            .Include(w => w.Product)
            .FirstOrDefaultAsync(w => w.Id == id);
        if (workOrder == null) return NotFound();
        return View(workOrder);
    }

    [HttpPost, ActionName("DeleteWorkOrder")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteWorkOrderConfirmed(int id)
    {
        var workOrder = await _context.WorkOrders.FindAsync(id);
        if (workOrder != null)
        {
            _context.WorkOrders.Remove(workOrder);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(WorkOrders));
    }

    private bool WorkOrderExists(int id) => _context.WorkOrders.Any(e => e.Id == id);

    // Download Template Excel untuk Work Orders
    public IActionResult DownloadWorkOrderTemplate()
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Work Orders Template");
        
        // Header
        worksheet.Cell(1, 1).Value = "OrderNumber";
        worksheet.Cell(1, 2).Value = "ProductMaterialCode";
        worksheet.Cell(1, 3).Value = "TargetQuantity";
        worksheet.Cell(1, 4).Value = "PlannedDate";
        worksheet.Cell(1, 5).Value = "ShiftName";
        worksheet.Cell(1, 6).Value = "Status";
        
        // Style header
        var headerRange = worksheet.Range(1, 1, 1, 6);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = XLColor.LightBlue;
        headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        
        // Auto fit columns
        worksheet.Columns().AdjustToContents();
        
        // Add sample data (row 2)
        worksheet.Cell(2, 1).Value = "WO-2025-001";
        worksheet.Cell(2, 2).Value = "BRG-R12"; // Harus sesuai dengan MaterialCode Product di database
        worksheet.Cell(2, 3).Value = 1000;
        worksheet.Cell(2, 4).Value = DateTime.Now.ToString("yyyy-MM-dd"); // Format: yyyy-MM-dd
        worksheet.Cell(2, 5).Value = "Shift 1"; // Harus sesuai dengan Shift Name di database
        worksheet.Cell(2, 6).Value = "Planned"; // Planned, InProgress, atau Completed
        
        // Add instruction sheet
        var instructionSheet = workbook.Worksheets.Add("Instructions");
        instructionSheet.Cell(1, 1).Value = "PETUNJUK PENGGUNAAN TEMPLATE";
        instructionSheet.Cell(1, 1).Style.Font.Bold = true;
        instructionSheet.Cell(1, 1).Style.Font.FontSize = 14;
        
        instructionSheet.Cell(3, 1).Value = "1. OrderNumber: Nomor Work Order (wajib, unique)";
        instructionSheet.Cell(4, 1).Value = "2. ProductMaterialCode: Kode Material Product (wajib, harus sesuai dengan MaterialCode di database)";
        instructionSheet.Cell(5, 1).Value = "3. TargetQuantity: Target jumlah produksi (wajib, angka)";
        instructionSheet.Cell(6, 1).Value = "4. PlannedDate: Tanggal rencana produksi (wajib, format: yyyy-MM-dd, contoh: 2025-12-17)";
        instructionSheet.Cell(7, 1).Value = "5. ShiftName: Nama Shift (wajib, harus sesuai dengan Shift Name di database)";
        instructionSheet.Cell(8, 1).Value = "6. Status: Status Work Order (wajib, pilihan: Planned, InProgress, Completed)";
        
        instructionSheet.Cell(10, 1).Value = "CATATAN PENTING:";
        instructionSheet.Cell(10, 1).Style.Font.Bold = true;
        instructionSheet.Cell(11, 1).Value = "- ProductMaterialCode harus sesuai dengan MaterialCode Product yang ada di database";
        instructionSheet.Cell(12, 1).Value = "- ShiftName harus sesuai dengan Shift Name yang ada di database";
        instructionSheet.Cell(13, 1).Value = "- PlannedDate format: yyyy-MM-dd (contoh: 2025-12-17)";
        instructionSheet.Cell(14, 1).Value = "- Jika OrderNumber sudah ada, data akan di-update";
        instructionSheet.Cell(15, 1).Value = "- Jika OrderNumber belum ada, data akan ditambahkan sebagai Work Order baru";
        instructionSheet.Cell(16, 1).Value = "- Status harus salah satu dari: Planned, InProgress, Completed";
        
        instructionSheet.Columns().AdjustToContents();
        
        // Save to stream
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();
        
        var fileName = $"WorkOrderTemplate_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
    }

    // Upload & Process Excel untuk Work Orders
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadWorkOrderExcel(IFormFile excelFile)
    {
        if (excelFile == null || excelFile.Length == 0)
        {
            TempData["UploadError"] = "File tidak boleh kosong.";
            return RedirectToAction(nameof(WorkOrders));
        }
        
        if (!Path.GetExtension(excelFile.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetExtension(excelFile.FileName).Equals(".xls", StringComparison.OrdinalIgnoreCase))
        {
            TempData["UploadError"] = "Format file harus .xlsx atau .xls";
            return RedirectToAction(nameof(WorkOrders));
        }
        
        try
        {
            int addedCount = 0;
            int updatedCount = 0;
            int errorCount = 0;
            var errors = new List<string>();
            
            using var stream = new MemoryStream();
            await excelFile.CopyToAsync(stream);
            
            using var workbook = new XLWorkbook(stream);
            var worksheet = workbook.Worksheet("Work Orders Template");
            
            if (worksheet == null)
            {
                TempData["UploadError"] = "Sheet 'Work Orders Template' tidak ditemukan. Pastikan menggunakan template yang benar.";
                return RedirectToAction(nameof(WorkOrders));
            }
            
            // Get all products for lookup by MaterialCode
            var products = await _context.Products.ToDictionaryAsync(p => p.MaterialCode, p => p.Id);
            
            // Get all shifts for lookup by Name
            var shifts = await _context.Shifts.ToDictionaryAsync(s => s.Name, s => s.Id);
            
            // Process rows (skip header)
            var rows = worksheet.RowsUsed().Skip(1);
            
            foreach (var row in rows)
            {
                try
                {
                    var orderNumber = row.Cell(1).GetString().Trim();
                    var productMaterialCode = row.Cell(2).GetString().Trim();
                    var targetQuantityStr = row.Cell(3).GetString().Trim();
                    var plannedDateStr = row.Cell(4).GetString().Trim();
                    var shiftName = row.Cell(5).GetString().Trim();
                    var statusStr = row.Cell(6).GetString().Trim();
                    
                    // Validasi required fields
                    if (string.IsNullOrEmpty(orderNumber) || string.IsNullOrEmpty(productMaterialCode) || 
                        string.IsNullOrEmpty(targetQuantityStr) || string.IsNullOrEmpty(statusStr))
                    {
                        errors.Add($"Row {row.RowNumber()}: OrderNumber, ProductMaterialCode, TargetQuantity, dan Status wajib diisi.");
                        errorCount++;
                        continue;
                    }
                    
                    // Validasi ProductMaterialCode
                    if (!products.ContainsKey(productMaterialCode))
                    {
                        errors.Add($"Row {row.RowNumber()}: Product Material Code '{productMaterialCode}' tidak ditemukan.");
                        errorCount++;
                        continue;
                    }
                    
                    // Parse target quantity
                    if (!int.TryParse(targetQuantityStr, out int targetQuantity) || targetQuantity <= 0)
                    {
                        errors.Add($"Row {row.RowNumber()}: TargetQuantity harus berupa angka positif.");
                        errorCount++;
                        continue;
                    }
                    
                    // Parse planned date (optional, bisa kosong)
                    DateTime? plannedDate = null;
                    if (!string.IsNullOrEmpty(plannedDateStr))
                    {
                        if (DateTime.TryParse(plannedDateStr, out DateTime parsedDate))
                        {
                            plannedDate = parsedDate.Date;
                        }
                        else
                        {
                            errors.Add($"Row {row.RowNumber()}: PlannedDate format tidak valid. Gunakan format yyyy-MM-dd.");
                            errorCount++;
                            continue;
                        }
                    }
                    
                    // Parse shift (optional, bisa kosong)
                    int? shiftId = null;
                    if (!string.IsNullOrEmpty(shiftName))
                    {
                        if (shifts.ContainsKey(shiftName))
                        {
                            shiftId = shifts[shiftName];
                        }
                        else
                        {
                            errors.Add($"Row {row.RowNumber()}: Shift Name '{shiftName}' tidak ditemukan.");
                            errorCount++;
                            continue;
                        }
                    }
                    
                    // Parse status
                    if (!Enum.TryParse<WorkOrderStatus>(statusStr, true, out WorkOrderStatus status))
                    {
                        errors.Add($"Row {row.RowNumber()}: Status harus salah satu dari: Planned, InProgress, Completed.");
                        errorCount++;
                        continue;
                    }
                    
                    // Check if work order exists
                    var existingWorkOrder = await _context.WorkOrders
                        .FirstOrDefaultAsync(w => w.OrderNumber == orderNumber);
                    
                    if (existingWorkOrder != null)
                    {
                        // Update existing
                        existingWorkOrder.ProductId = products[productMaterialCode];
                        existingWorkOrder.TargetQuantity = targetQuantity;
                        existingWorkOrder.Status = status;
                        existingWorkOrder.PlannedDate = plannedDate;
                        existingWorkOrder.ShiftId = shiftId;
                        updatedCount++;
                    }
                    else
                    {
                        // Add new
                        var newWorkOrder = new WorkOrder
                        {
                            OrderNumber = orderNumber,
                            ProductId = products[productMaterialCode],
                            TargetQuantity = targetQuantity,
                            Status = status,
                            PlannedDate = plannedDate,
                            ShiftId = shiftId
                        };
                        _context.WorkOrders.Add(newWorkOrder);
                        addedCount++;
                    }
                }
                catch (Exception ex)
                {
                    errors.Add($"Row {row.RowNumber()}: {ex.Message}");
                    errorCount++;
                }
            }
            
            await _context.SaveChangesAsync();
            
            var successMessage = $"Upload berhasil! Ditambahkan: {addedCount}, Diupdate: {updatedCount}";
            if (errorCount > 0)
            {
                successMessage += $", Error: {errorCount}";
            }
            
            TempData["UploadSuccess"] = successMessage;
            
            if (errors.Any())
            {
                TempData["UploadError"] = "Detail Error:<br/>" + string.Join("<br/>", errors.Take(10));
                if (errors.Count > 10)
                {
                    TempData["UploadError"] += $"<br/>... dan {errors.Count - 10} error lainnya.";
                }
            }
        }
        catch (Exception ex)
        {
            TempData["UploadError"] = $"Terjadi error: {ex.Message}";
        }
        
        return RedirectToAction(nameof(WorkOrders));
    }

    // ========== DOWNTIME REASONS CRUD ==========
    public async Task<IActionResult> DowntimeReasons()
    {
        var reasons = await _context.DowntimeReasons.ToListAsync();
        return View(reasons);
    }

    public IActionResult CreateDowntimeReason()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDowntimeReason(DowntimeReason reason)
    {
        if (ModelState.IsValid)
        {
            _context.Add(reason);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(DowntimeReasons));
        }
        return View(reason);
    }

    public async Task<IActionResult> EditDowntimeReason(int? id)
    {
        if (id == null) return NotFound();
        var reason = await _context.DowntimeReasons.FindAsync(id);
        if (reason == null) return NotFound();
        return View(reason);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditDowntimeReason(int id, DowntimeReason reason)
    {
        if (id != reason.Id) return NotFound();
        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(reason);
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!DowntimeReasonExists(reason.Id)) return NotFound();
                throw;
            }
            return RedirectToAction(nameof(DowntimeReasons));
        }
        return View(reason);
    }

    public async Task<IActionResult> DeleteDowntimeReason(int? id)
    {
        if (id == null) return NotFound();
        var reason = await _context.DowntimeReasons.FindAsync(id);
        if (reason == null) return NotFound();
        return View(reason);
    }

    [HttpPost, ActionName("DeleteDowntimeReason")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDowntimeReasonConfirmed(int id)
    {
        var reason = await _context.DowntimeReasons.FindAsync(id);
        if (reason != null)
        {
            _context.DowntimeReasons.Remove(reason);
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(DowntimeReasons));
    }

    private bool DowntimeReasonExists(int id) => _context.DowntimeReasons.Any(e => e.Id == id);

    // ========== API ENDPOINTS ==========
    // ✅ Endpoint untuk serve machine image dari filesystem berdasarkan ImageUrl di database
    [HttpGet]
    [Route("/api/machines/{machineId}/image")]
    public async Task<IActionResult> GetMachineImage(string machineId)
    {
        try
        {
            // Ambil ImageUrl dari database
            var machine = await _context.Machines
                .AsNoTracking()
                .Where(m => m.Id == machineId)
                .Select(m => new { m.ImageUrl })
                .FirstOrDefaultAsync();
            
            if (machine == null)
            {
                return NotFound(new { message = "Machine not found" });
            }
            
            // Serve dari filesystem berdasarkan ImageUrl di database
            if (!string.IsNullOrWhiteSpace(machine.ImageUrl))
            {
                var imagePath = machine.ImageUrl.TrimStart('/');
                var fullPath = Path.Combine(_environment.WebRootPath, imagePath);
                
                if (System.IO.File.Exists(fullPath))
                {
                    var fileBytes = await System.IO.File.ReadAllBytesAsync(fullPath);
                    var contentType = GetContentType(imagePath);
                    return File(fileBytes, contentType);
                }
            }
            
            return NotFound(new { 
                message = "Image not found. ImageUrl: " + (machine.ImageUrl ?? "null")
            });
        }
        catch (Exception ex)
        {
            // Log error untuk debugging
            System.Diagnostics.Debug.WriteLine($"Error in GetMachineImage: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"StackTrace: {ex.StackTrace}");
            
            return StatusCode(500, new { message = "Error loading image", error = ex.Message });
        }
    }
    
    // ✅ Helper method untuk menentukan content type
    private string GetContentType(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            _ => "application/octet-stream"
        };
    }

    // Helper method untuk query ManPowers dengan error handling
    private async Task<List<ManPower>> GetManPowersSafe()
    {
        try
        {
            return await _context.ManPowers
                .Where(m => m.IsActive)
                .OrderBy(m => m.Value)
                .ToListAsync();
        }
        catch
        {
            // Jika tabel belum ada, return empty list
            return new List<ManPower>();
        }
    }

    // ========== MAN POWER CRUD ==========
    public async Task<IActionResult> ManPowers()
    {
        try
        {
            var manPowers = await _context.ManPowers
                .Where(m => m.IsActive)
                .OrderBy(m => m.Value)
                .ToListAsync();
            return View(manPowers);
        }
        catch (Exception ex)
        {
            // Jika tabel belum ada, tampilkan pesan error yang user-friendly
            TempData["ErrorMessage"] = "Tabel ManPower belum tersedia. Silakan jalankan migration terlebih dahulu.";
            return View(new List<ManPower>());
        }
    }

    public IActionResult CreateManPower()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateManPower(ManPower manPower)
    {
        if (ModelState.IsValid)
        {
            try
            {
                _context.ManPowers.Add(manPower);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Man Power berhasil ditambahkan.";
                return RedirectToAction(nameof(ManPowers));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    ModelState.AddModelError("", $"Detail: {ex.InnerException.Message}");
                }
            }
        }
        return View(manPower);
    }

    public async Task<IActionResult> EditManPower(int? id)
    {
        if (id == null) return NotFound();
        try
        {
            var manPower = await _context.ManPowers.FindAsync(id);
            if (manPower == null) return NotFound();
            return View(manPower);
        }
        catch
        {
            TempData["ErrorMessage"] = "Tabel ManPower belum tersedia. Silakan jalankan migration terlebih dahulu.";
            return RedirectToAction(nameof(ManPowers));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditManPower(int id, ManPower manPower)
    {
        if (id != manPower.Id) return NotFound();
        
        if (ModelState.IsValid)
        {
            try
            {
                _context.Update(manPower);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Man Power berhasil diupdate.";
                return RedirectToAction(nameof(ManPowers));
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await _context.ManPowers.AnyAsync(m => m.Id == id))
                    return NotFound();
                throw;
            }
        }
        return View(manPower);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteManPower(int id)
    {
        try
        {
            var manPower = await _context.ManPowers.FindAsync(id);
            if (manPower != null)
            {
                manPower.IsActive = false; // Soft delete
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Man Power berhasil dihapus.";
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error menghapus Man Power: {ex.Message}";
        }
        return RedirectToAction(nameof(ManPowers));
    }

    // ========== INDEX (Admin Dashboard) ==========
    public IActionResult Index()
    {
        return View();
    }
}

