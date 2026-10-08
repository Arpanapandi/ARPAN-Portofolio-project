using SistemPacking.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemPacking.Web.Models;
using SistemPacking.Web.Interfaces;
using SistemPacking.Web.Extensions;
using SistemPacking.Web.Filters;

namespace SistemPacking.Web.Controllers;

[CustomAuthorize("ManageMaster")]
public class MasterController : Controller
{
    private readonly IUnitOfWork _uow;
    private readonly IWebHostEnvironment _env;
    private readonly SistemPacking.Web.Services.IMasterExportImportService _importExportService;

    public MasterController(IUnitOfWork uow, IWebHostEnvironment env, SistemPacking.Web.Services.IMasterExportImportService importExportService)
    {
        _uow = uow;
        _env = env;
        _importExportService = importExportService;
    }

    private void SetUser() => ViewBag.User = HttpContext.Session.GetUserSession()!;

    // ============ ITEMS ============
    public async Task<IActionResult> Items()
    {
        SetUser();
        var items = await _uow.Items.GetWithDetailsAsync();
        return View(items.Where(i => i.Category == "Export"));
    }

    [HttpGet]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> CheckDb()
    {
        var items = await _uow.Items.GetWithDetailsAsync();
        return Json(items.Select(i => new { i.Id, i.ItemCode, i.Category, i.IsDeleted }));
    }

    [HttpGet]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> SeedDummyItems([FromServices] SistemPacking.Web.Data.ApplicationDbContext db)
    {
        var existing = await db.Items.IgnoreQueryFilters().ToListAsync();
        
        var instructions = new string[] {
            "Gunakan karton A1", "Gunakan karton B2", "Gunakan karton C3", "Gunakan tabung", "Gunakan kotak pallet",
            "Bungkus 1 lapis bubble wrap", "Bungkus 2 lapis bubble wrap", "Gunakan styrofoam pelindung", "Gunakan PE foam", "Bungkus plastik seal",
            "Masukkan part miring 45 derajat", "Susun rata menyilang", "Masukkan posisi berdiri", "Tidurkan part dalam kotak", "Posisikan konektor di atas",
            "Segel dengan lakban bening", "Segel silang dengan lakban coklat", "Gunakan strapping band", "Staples ujung karton", "Lem tembak sisi pinggir",
            "Tempel Label QC di pojok kanan", "Tempel Barcode di tengah", "Tempel stiker Fragile", "Beri stempel lulus inspeksi", "Sisipkan kartu garansi"
        };

        int updated = 0;
        int added = 0;
        for (int i = 1; i <= 20; i++)
        {
            string itemCode = $"SP-{i:D4}";
            var item = existing.FirstOrDefault(x => x.ItemCode == itemCode);
            bool isNew = false;
            
            if (item == null)
            {
                item = new Item();
                item.ItemCode = itemCode;
                item.Barcode = itemCode;
                item.CustomerId = 1;
                item.CreatedAt = DateTime.Now;
                isNew = true;
            }

            item.ItemName = $"Dummy Part {i}";
            item.VIN = $"VIN{i:D4}";
            item.Dock = $"DOCK-{i % 3 + 1}";
            item.TypeKarton = $"KARTON-{i % 3 + 1}";
            item.TypePlastik = $"PLASTIK-{i % 2 + 1}";
            item.Point1 = instructions[i % 5];
            item.Point2 = instructions[5 + (i % 5)];
            item.Point3 = instructions[10 + (i % 5)];
            item.Point4 = instructions[15 + (i % 5)];
            item.Point5 = instructions[20 + (i % 5)];
            item.Category = "Export"; // Changed to Export to show in Master Item Export
            item.Status = ItemStatus.Active;
            item.SpisImagePath = $"/images/std-packing/spis/{item.ItemCode}.png";
            item.SppsImagePath = $"/images/std-packing/spps/{item.ItemCode}.png";
            item.IsDeleted = false;
            
            if (isNew)
            {
                db.Items.Add(item);
                added++;
            }
            else
            {
                db.Items.Update(item);
                updated++;
            }
        }

        await db.SaveChangesAsync();
        return Json(new { success = true, added = added, updated = updated, message = "Generated 20 dummy items" });
    }

    [HttpGet]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> UpdateItemDummyData([FromServices] SistemPacking.Web.Data.ApplicationDbContext db)
    {
        var items = await db.Items.IgnoreQueryFilters().ToListAsync();
        var plants = new[] { "SUNTER", "KARAWANG", "CIBITUNG" };
        var racks = new[] { "RACK-A", "RACK-B", "RACK-C", "RACK-D" };
        var random = new Random();
        
        int updated = 0;
        foreach (var item in items)
        {
            item.ProdPlant = plants[random.Next(plants.Length)];
            item.LokasiRack = $"LOK-{random.Next(1, 10)}";
            item.Rack = racks[random.Next(racks.Length)];
            item.NoRack = $"NO-{random.Next(1, 100):D3}";
            item.QtyPcs = random.Next(10, 500);
            item.ActQty = item.QtyPcs;
            item.Min = 0.5M;
            item.Rop = 1M;
            item.Max = 2M;
            
            db.Items.Update(item);
            updated++;
        }
        await db.SaveChangesAsync();
        return Json(new { success = true, updated = updated, message = "Master Item dummy data updated" });
    }

    [HttpGet]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> GenerateDummyCustomers([FromServices] SistemPacking.Web.Data.ApplicationDbContext db)
    {
        var existingCodes = await db.Customers.IgnoreQueryFilters().Select(c => c.CustomerCode).ToListAsync();
        int added = 0;
        for (int i = 1; i <= 10; i++)
        {
            var code = $"CUST-{i:D3}";
            if (!existingCodes.Contains(code))
            {
                db.Customers.Add(new Customer
                {
                    CustomerCode = code,
                    CustomerName = $"PT. Dummy Customer {i}",
                    Address = $"Jl. Dummy No. {i}, Jakarta",
                    ContactPerson = $"Bapak Dummy {i}",
                    Phone = $"0812345678{i:D2}",
                    IsActive = true
                });
                added++;
            }
        }
        await db.SaveChangesAsync();
        return Json(new { success = true, added = added, message = "10 Dummy Customers generated" });
    }

    [HttpGet("Master/FixDbItems")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> FixDbItems([FromServices] SistemPacking.Web.Data.ApplicationDbContext db)
    {
        var items = await db.Items.IgnoreQueryFilters().ToListAsync();
        foreach(var i in items)
        {
            i.ActQty = 0;
            i.IsDeleted = false;
            i.CustomerId = 1;
        }
        db.Items.UpdateRange(items);
        await db.SaveChangesAsync();
        return Json(new { success = true, count = items.Count, message = "DB Fixed!" });
    }

    [HttpGet("Master/TestEF")]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> TestEF([FromServices] SistemPacking.Web.Data.ApplicationDbContext db)
    {
        var rawCount = await db.Items.IgnoreQueryFilters().CountAsync();
        var normalCount = await db.Items.CountAsync();
        var includeCustomer = await db.Items.Include(i => i.Customer).CountAsync();
        var includeAll = await db.Items.Include(i => i.Customer).Include(i => i.FGLocation).ThenInclude(l => l.Area).CountAsync();
        return Json(new { rawCount, normalCount, includeCustomer, includeAll });
    }

    [HttpGet]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public async Task<IActionResult> SeedDummySparePartItems([FromServices] SistemPacking.Web.Data.ApplicationDbContext db)
    {
        var existing = await db.Items.IgnoreQueryFilters().ToListAsync();
        
        var instructions = new string[] {
            "Gunakan karton SP1", "Gunakan karton SP2", "Gunakan karton SP3", "Gunakan tabung kecil", "Gunakan pallet mini",
            "Bungkus 3 lapis bubble wrap", "Bungkus 4 lapis bubble wrap", "Gunakan busa pelindung", "Gunakan plastik anti-statis", "Bungkus shrink film",
            "Pastikan posisi rata", "Susun bertingkat", "Letakkan konektor di bawah", "Ganjal sisi kosong", "Hindari tumpukan tinggi",
            "Segel lakban merah", "Segel lakban kuning", "Ikat dengan tali tebal", "Staples 4 sisi", "Lem dengan perekat kuat",
            "Label QC di atas", "Barcode di samping", "Stiker Jangan Dibanting", "Stempel QC Pass", "Manual panduan terlampir"
        };

        int updated = 0;
        int added = 0;
        for (int i = 1; i <= 20; i++)
        {
            string itemCode = $"SPSP-{i:D4}";
            var item = existing.FirstOrDefault(x => x.ItemCode == itemCode);
            bool isNew = false;
            
            if (item == null)
            {
                item = new Item();
                item.ItemCode = itemCode;
                item.Barcode = itemCode;
                item.CustomerId = 1;
                item.CreatedAt = DateTime.Now;
                isNew = true;
            }

            item.ItemName = $"Spare Part Dummy {i}";
            item.VIN = $"VIN-SP{i:D4}";
            item.Dock = $"DOCK-{i % 4 + 1}";
            item.TypeKarton = $"KARTON-{i % 4 + 1}";
            item.TypePlastik = $"PLASTIK-{i % 3 + 1}";
            item.Point1 = instructions[i % 5];
            item.Point2 = instructions[5 + (i % 5)];
            item.Point3 = instructions[10 + (i % 5)];
            item.Point4 = instructions[15 + (i % 5)];
            item.Point5 = instructions[20 + (i % 5)];
            item.Category = "SparePart";
            item.Status = ItemStatus.Active;
            item.SpisImagePath = $"/images/std-packing/spis/{item.ItemCode}.png";
            item.SppsImagePath = $"/images/std-packing/spps/{item.ItemCode}.png";
            item.IsDeleted = false;
            
            if (isNew)
            {
                db.Items.Add(item);
                added++;
            }
            else
            {
                db.Items.Update(item);
                updated++;
            }
        }

        await db.SaveChangesAsync();
        return Json(new { success = true, added = added, updated = updated, message = "Generated 20 dummy spare part items" });
    }

    // ============ ITEMS SPARE PART ============
    public async Task<IActionResult> Items_SP()
    {
        SetUser();
        var items = await _uow.Items.GetWithDetailsAsync();
        return View(items.Where(i => i.Category == "SparePart"));
    }

    // ============ MASTER ITEM (COMBINED) ============
    public async Task<IActionResult> MasterItem()
    {
        SetUser();
        var items = await _uow.Items.GetWithDetailsAsync();
        return View(items); // Return all items regardless of category
    }

    // ============ ITEM MAPPING ============
    public async Task<IActionResult> ItemMapping()
    {
        SetUser();
        var mappings = (await _uow.ItemMappings.GetAllAsync())
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.ItemId)
            .ToList();
        // Load Item navigation
        var items = (await _uow.Items.GetWithDetailsAsync()).ToList();
        foreach (var m in mappings)
            m.Item = items.FirstOrDefault(i => i.Id == m.ItemId)!;
        ViewBag.Items = items;
        return View(mappings);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveItemMapping([FromForm] int Id, [FromForm] int ItemId, [FromForm] string KanbanCode, [FromForm] string? Description)
    {
        try
        {
            if (Id == 0)
            {
                var entity = new SistemPacking.Web.Models.ItemMapping
                {
                    ItemId = ItemId,
                    KanbanCode = KanbanCode,
                    Description = Description,
                    IsActive = true,
                    CreatedAt = DateTime.Now
                };
                await _uow.ItemMappings.AddAsync(entity);
            }
            else
            {
                var entity = await _uow.ItemMappings.GetByIdAsync(Id);
                if (entity == null) return Json(new { success = false, message = "Data tidak ditemukan" });
                entity.ItemId = ItemId;
                entity.KanbanCode = KanbanCode;
                entity.Description = Description;
                entity.UpdatedAt = DateTime.Now;
                await _uow.ItemMappings.UpdateAsync(entity);
            }
            await _uow.SaveChangesAsync();
            return Json(new { success = true, message = "Data berhasil disimpan" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItemMapping([FromForm] int id)
    {
        try
        {
            var entity = await _uow.ItemMappings.GetByIdAsync(id);
            if (entity == null) return Json(new { success = false, message = "Data tidak ditemukan" });
            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.Now;
            await _uow.ItemMappings.UpdateAsync(entity);
            await _uow.SaveChangesAsync();
            return Json(new { success = true, message = "Data berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }


    [HttpGet]
    public async Task<IActionResult> ExportItemMappings()
    {
        var fileContent = await _importExportService.ExportItemMappingsAsync();
        return File(fileContent, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ItemMappings_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpGet]
    public IActionResult DownloadTemplateItemMapping()
    {
        var fileContent = _importExportService.GenerateItemMappingTemplate();
        return File(fileContent, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_ItemMappings.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportItemMappings(IFormFile file)
    {
        if (file == null || file.Length <= 0) return Json(new { success = false, message = "File tidak valid" });
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) return Json(new { success = false, message = "Format file harus .xlsx" });

        using var stream = file.OpenReadStream();
        var result = await _importExportService.ImportItemMappingsAsync(stream);
        return Json(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDeleteItemMappings([FromBody] int[] ids)
    {
        try
        {
            if (ids == null || ids.Length == 0) return Json(new { success = false, message = "Tidak ada data yang dipilih" });
            foreach (var id in ids)
            {
                var entity = await _uow.ItemMappings.GetByIdAsync(id);
                if (entity != null)
                {
                    entity.IsDeleted = true;
                    entity.UpdatedAt = DateTime.Now;
                    await _uow.ItemMappings.UpdateAsync(entity);
                }
            }
            await _uow.SaveChangesAsync();
            return Json(new { success = true, message = $"{ids.Length} data berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetItems()
        => Json(await _uow.Items.GetWithDetailsAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveItem([FromForm] Item model, IFormFile? spisImage, IFormFile? sppsImage)
    {
        string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "items");
        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

        if (spisImage != null)
        {
            string fileName = Guid.NewGuid().ToString() + "_" + spisImage.FileName;
            string filePath = Path.Combine(uploadsFolder, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await spisImage.CopyToAsync(stream);
            }
            model.SpisImagePath = "/uploads/items/" + fileName;
        }

        if (sppsImage != null)
        {
            string fileName = Guid.NewGuid().ToString() + "_" + sppsImage.FileName;
            string filePath = Path.Combine(uploadsFolder, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await sppsImage.CopyToAsync(stream);
            }
            model.SppsImagePath = "/uploads/items/" + fileName;
        }

        if (model.Id == 0)
        {
            await _uow.Items.AddAsync(model);
        }
        else
        {
            var existing = await _uow.Items.GetByIdAsync(model.Id);
            if (existing == null) return Json(new { success = false, message = "Item tidak ditemukan." });
            existing.ItemCode = model.ItemCode; existing.ItemName = model.ItemName;
            existing.Barcode = model.Barcode; existing.CustomerId = model.CustomerId;
            existing.UOM = model.UOM; existing.PackingStandard = model.PackingStandard;
            existing.FGLocationId = model.FGLocationId; existing.Status = model.Status;
            
            existing.VIN = model.VIN;
            existing.Dock = model.Dock;
            existing.ProdPlant = model.ProdPlant;
            existing.LokasiRack = model.LokasiRack;
            existing.Rack = model.Rack;
            existing.NoRack = model.NoRack;
            existing.QtyPcs = model.QtyPcs;
            existing.Min = model.Min;
            existing.Rop = model.Rop;
            existing.Max = model.Max;
            
            existing.Point1 = model.Point1;
            existing.Point2 = model.Point2;
            existing.Point3 = model.Point3;
            existing.Point4 = model.Point4;
            existing.Point5 = model.Point5;
            existing.TypeKarton = model.TypeKarton;
            existing.TypePlastik = model.TypePlastik;
            
            if (spisImage != null) existing.SpisImagePath = model.SpisImagePath;
            if (sppsImage != null) existing.SppsImagePath = model.SppsImagePath;

            await _uow.Items.UpdateAsync(existing);
        }
        await _uow.SaveChangesAsync();
        return Json(new { success = true, message = "Item berhasil disimpan." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteItem(int id)
    {
        await _uow.Items.DeleteAsync(id);
        await _uow.SaveChangesAsync();
        return Json(new { success = true, message = "Item berhasil dihapus." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDeleteItems([FromBody] List<int> ids)
    {
        if (ids == null || !ids.Any()) return Json(new { success = false, message = "Tidak ada item yang dipilih." });
        foreach (var id in ids)
        {
            await _uow.Items.DeleteAsync(id);
        }
        await _uow.SaveChangesAsync();
        return Json(new { success = true, message = $"{ids.Count} item berhasil dihapus." });
    }

    [HttpGet]
    public IActionResult DownloadTemplateItem()
    {
        var fileBytes = _importExportService.GenerateItemTemplate();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_Item.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportItems()
    {
        var fileBytes = await _importExportService.ExportItemsAsync();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Master_Items_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportItems(IFormFile file, IFormFile? spisZip, IFormFile? sppsZip)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "File tidak valid." });

        string uploadsFolder = Path.Combine(_env.WebRootPath, "uploads", "items");
        if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

        var spisMap = new Dictionary<string, string>();
        if (spisZip != null && spisZip.Length > 0)
        {
            using var stream = spisZip.OpenReadStream();
            using var archive = new System.IO.Compression.ZipArchive(stream);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0 || string.IsNullOrEmpty(entry.Name)) continue;
                string ext = Path.GetExtension(entry.Name).ToLower();
                if (ext == ".jpg" || ext == ".png" || ext == ".jpeg")
                {
                    string fileName = Guid.NewGuid().ToString() + "_" + entry.Name;
                    string filePath = Path.Combine(uploadsFolder, fileName);
                    using var entryStream = entry.Open();
                    using var fileStream = new FileStream(filePath, FileMode.Create);
                    await entryStream.CopyToAsync(fileStream);
                    string key = Path.GetFileNameWithoutExtension(entry.Name).ToUpper();
                    spisMap[key] = "/uploads/items/" + fileName;
                }
            }
        }

        var sppsMap = new Dictionary<string, string>();
        if (sppsZip != null && sppsZip.Length > 0)
        {
            using var stream = sppsZip.OpenReadStream();
            using var archive = new System.IO.Compression.ZipArchive(stream);
            foreach (var entry in archive.Entries)
            {
                if (entry.Length == 0 || string.IsNullOrEmpty(entry.Name)) continue;
                string ext = Path.GetExtension(entry.Name).ToLower();
                if (ext == ".jpg" || ext == ".png" || ext == ".jpeg")
                {
                    string fileName = Guid.NewGuid().ToString() + "_" + entry.Name;
                    string filePath = Path.Combine(uploadsFolder, fileName);
                    using var entryStream = entry.Open();
                    using var fileStream = new FileStream(filePath, FileMode.Create);
                    await entryStream.CopyToAsync(fileStream);
                    string key = Path.GetFileNameWithoutExtension(entry.Name).ToUpper();
                    sppsMap[key] = "/uploads/items/" + fileName;
                }
            }
        }

        using var excelStream = file.OpenReadStream();
        var result = await _importExportService.ImportItemsAsync(excelStream, spisMap, sppsMap);
        return Json(result);
    }

    // ============ CUSTOMERS ============
    public async Task<IActionResult> Customers([FromServices] SistemPacking.Web.Data.ApplicationDbContext db)
    {
        SetUser();
        var customers = await db.Customers.Where(c => !c.IsDeleted).ToListAsync();
        return View(customers);
    }

    [HttpGet]
    public async Task<IActionResult> GetCustomers([FromServices] SistemPacking.Web.Data.ApplicationDbContext db)
    {
        var customers = await db.Customers.ToListAsync();
        return Json(customers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveCustomer([FromBody] Customer model)
    {
        if (model.Id == 0)
            await _uow.Customers.AddAsync(model);
        else
        {
            var existing = await _uow.Customers.GetByIdAsync(model.Id);
            if (existing == null) return Json(new { success = false });
            existing.CustomerCode = model.CustomerCode; existing.CustomerName = model.CustomerName;
            existing.Address = model.Address; existing.ContactPerson = model.ContactPerson;
            existing.Phone = model.Phone; existing.IsActive = model.IsActive;
            
            // New Fields
            existing.Dock = model.Dock;
            existing.Route = model.Route;
            existing.Cycle = model.Cycle;
            existing.StdShopping = model.StdShopping;
            existing.StdPacking = model.StdPacking;
            existing.StdPickup = model.StdPickup;

            await _uow.Customers.UpdateAsync(existing);
        }
        await _uow.SaveChangesAsync();
        return Json(new { success = true, message = "Customer berhasil disimpan." });
    }

    [HttpGet]
    public IActionResult DownloadTemplateCustomer()
    {
        var fileBytes = _importExportService.GenerateCustomerTemplate();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_Customer.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportCustomers()
    {
        var fileBytes = await _importExportService.ExportCustomersAsync();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Master_Customers_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportCustomers(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "File tidak valid." });

        using var stream = file.OpenReadStream();
        var result = await _importExportService.ImportCustomersAsync(stream);
        return Json(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDeleteCustomers([FromBody] int[] ids)
    {
        try
        {
            if (ids == null || ids.Length == 0) return Json(new { success = false, message = "Tidak ada data yang dipilih" });
            foreach (var id in ids)
            {
                var entity = await _uow.Customers.GetByIdAsync(id);
                if (entity != null)
                {
                    entity.IsDeleted = true;
                    entity.UpdatedAt = DateTime.Now;
                    await _uow.Customers.UpdateAsync(entity);
                }
            }
            await _uow.SaveChangesAsync();
            return Json(new { success = true, message = $"{ids.Length} data berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    // ============ USERS ============
    [CustomAuthorize("ManageUser")]
    public async Task<IActionResult> Users()
    {
        SetUser();
        return View(await _uow.Users.GetWithRolesAsync());
    }

    [HttpGet]
    public async Task<IActionResult> GetUsers()
        => Json(await _uow.Users.GetWithRolesAsync());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveUser([FromBody] SaveUserRequest req)
    {
        if (req.Id == 0)
        {
            var user = new User
            {
                NIK = req.NIK, Username = req.Username, FullName = req.FullName,
                RoleId = req.RoleId, IsActive = req.IsActive, Email = req.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password ?? "Operator@123")
            };
            await _uow.Users.AddAsync(user);
        }
        else
        {
            var existing = await _uow.Users.GetByIdAsync(req.Id);
            if (existing == null) return Json(new { success = false });
            existing.NIK = req.NIK; existing.Username = req.Username;
            existing.FullName = req.FullName; existing.RoleId = req.RoleId;
            existing.IsActive = req.IsActive; existing.Email = req.Email;
            if (!string.IsNullOrEmpty(req.Password))
                existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.Password);
            await _uow.Users.UpdateAsync(existing);
        }
        await _uow.SaveChangesAsync();
        return Json(new { success = true, message = "User berhasil disimpan." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(int id)
    {
        await _uow.Users.DeleteAsync(id);
        await _uow.SaveChangesAsync();
        return Json(new { success = true });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDeleteUsers([FromBody] int[] ids)
    {
        try
        {
            if (ids == null || ids.Length == 0) return Json(new { success = false, message = "Tidak ada data yang dipilih" });
            foreach (var id in ids)
            {
                var entity = await _uow.Users.GetByIdAsync(id);
                if (entity != null)
                {
                    entity.IsDeleted = true;
                    entity.UpdatedAt = DateTime.Now;
                    await _uow.Users.UpdateAsync(entity);
                }
            }
            await _uow.SaveChangesAsync();
            return Json(new { success = true, message = $"{ids.Length} data berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = "Terjadi kesalahan: " + ex.Message });
        }
    }

    // ============ ROLES ============
    public async Task<IActionResult> Roles()
    {
        SetUser();
        return View(await _uow.Roles.GetAllAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveRole([FromBody] Role model)
    {
        if (model.Id == 0)
        {
            model.CreatedAt = DateTime.Now;
            await _uow.Roles.AddAsync(model);
        }
        else
        {
            var existing = await _uow.Roles.GetByIdAsync(model.Id);
            if (existing == null) return Json(new { success = false, message = "Role tidak ditemukan" });
            
            existing.RoleName = model.RoleName;
            existing.CanManageMaster = model.CanManageMaster;
            existing.CanManageOrder = model.CanManageOrder;
            existing.CanShopping = model.CanShopping;
            existing.CanPacking = model.CanPacking;
            existing.CanVerify = model.CanVerify;
            existing.CanViewReport = model.CanViewReport;
            existing.CanManageUser = model.CanManageUser;
            existing.UpdatedAt = DateTime.Now;

            await _uow.Roles.UpdateAsync(existing);
        }
        await _uow.SaveChangesAsync();
        return Json(new { success = true, message = "Role berhasil disimpan." });
    }

    // ============ AREAS & LOCATIONS ============
    public async Task<IActionResult> Areas()
    {
        SetUser();
        return View(await _uow.Areas.GetAllAsync());
    }

    public async Task<IActionResult> FGLocations()
    {
        SetUser();
        ViewBag.Areas = await _uow.Areas.GetAllAsync();
        return View(await _uow.FGLocations.GetAllAsync());
    }

    // ============ SHIFTS ============
    public async Task<IActionResult> Shifts()
    {
        SetUser();
        return View(await _uow.Shifts.GetAllAsync());
    }

    // ============ REASON REJECT ============
    public async Task<IActionResult> ReasonRejects()
    {
        SetUser();
        return View(await _uow.ReasonRejects.GetAllAsync());
    }

    // ============ ALL IMPORT / EXPORT ENDPOINTS ============

    [HttpGet]
    public IActionResult DownloadTemplateArea()
    {
        var fileBytes = _importExportService.GenerateAreaTemplate();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_Area.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportAreas()
    {
        var fileBytes = await _importExportService.ExportAreasAsync();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Master_Areas_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportAreas(IFormFile file)
    {
        if (file == null || file.Length == 0) return Json(new { success = false, message = "File tidak valid." });
        using var stream = file.OpenReadStream();
        return Json(await _importExportService.ImportAreasAsync(stream));
    }

    [HttpGet]
    public IActionResult DownloadTemplateFGLocation()
    {
        var fileBytes = _importExportService.GenerateFGLocationTemplate();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_FGLocation.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportFGLocations()
    {
        var fileBytes = await _importExportService.ExportFGLocationsAsync();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Master_FGLocations_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportFGLocations(IFormFile file)
    {
        if (file == null || file.Length == 0) return Json(new { success = false, message = "File tidak valid." });
        using var stream = file.OpenReadStream();
        return Json(await _importExportService.ImportFGLocationsAsync(stream));
    }

    [HttpGet]
    public IActionResult DownloadTemplateShift()
    {
        var fileBytes = _importExportService.GenerateShiftTemplate();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_Shift.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportShifts()
    {
        var fileBytes = await _importExportService.ExportShiftsAsync();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Master_Shifts_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportShifts(IFormFile file)
    {
        if (file == null || file.Length == 0) return Json(new { success = false, message = "File tidak valid." });
        using var stream = file.OpenReadStream();
        return Json(await _importExportService.ImportShiftsAsync(stream));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDeleteShifts([FromBody] int[] ids)
    {
        try
        {
            if (ids == null || ids.Length == 0) return Json(new { success = false, message = "Tidak ada data yang dipilih" });
            foreach (var id in ids)
            {
                var entity = await _uow.Shifts.GetByIdAsync(id);
                if (entity != null)
                {
                    entity.IsDeleted = true;
                    entity.UpdatedAt = DateTime.Now;
                    await _uow.Shifts.UpdateAsync(entity);
                }
            }
            await _uow.SaveChangesAsync();
            return Json(new { success = true, message = $"{ids.Length} data berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult DownloadTemplateReasonReject()
    {
        var fileBytes = _importExportService.GenerateReasonRejectTemplate();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_ReasonReject.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportReasonRejects()
    {
        var fileBytes = await _importExportService.ExportReasonRejectsAsync();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Master_ReasonRejects_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportReasonRejects(IFormFile file)
    {
        if (file == null || file.Length == 0) return Json(new { success = false, message = "File tidak valid." });
        using var stream = file.OpenReadStream();
        return Json(await _importExportService.ImportReasonRejectsAsync(stream));
    }

    [HttpGet]
    public IActionResult DownloadTemplateUser()
    {
        var fileBytes = _importExportService.GenerateUserTemplate();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Master_User.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> ExportUsers()
    {
        var fileBytes = await _importExportService.ExportUsersAsync();
        return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Master_Users_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportUsers(IFormFile file)
    {
        if (file == null || file.Length == 0) return Json(new { success = false, message = "File tidak valid." });
        using var stream = file.OpenReadStream();
        return Json(await _importExportService.ImportUsersAsync(stream));
    }

    // ============ STD PACKING ============
    public IActionResult StdPacking()
    {
        SetUser();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadStdPackingZip(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "File zip tidak valid." });

        if (!file.FileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            return Json(new { success = false, message = "Harap upload file dengan ekstensi .zip" });

        var stdPackingPath = Path.Combine(_env.WebRootPath, "images", "std-packing");
        var spisPath = Path.Combine(stdPackingPath, "spis");
        var sppsPath = Path.Combine(stdPackingPath, "spps");

        if (!Directory.Exists(spisPath)) Directory.CreateDirectory(spisPath);
        if (!Directory.Exists(sppsPath)) Directory.CreateDirectory(sppsPath);

        int spisCount = 0;
        int sppsCount = 0;
        int skippedCount = 0;

        try
        {
            using var stream = file.OpenReadStream();
            using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
            
            var validExtensions = new[] { ".jpg", ".jpeg", ".png" };

            foreach (var entry in archive.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directories

                var ext = Path.GetExtension(entry.Name).ToLower();
                if (!validExtensions.Contains(ext))
                {
                    skippedCount++;
                    continue;
                }

                // Determine SPIS or SPPS based on folder in zip
                var entryPath = entry.FullName.Replace("\\", "/").ToUpper();
                string? targetFolder = null;
                
                if (entryPath.Contains("SPIS/"))
                {
                    targetFolder = spisPath;
                    spisCount++;
                }
                else if (entryPath.Contains("SPPS/"))
                {
                    targetFolder = sppsPath;
                    sppsCount++;
                }
                else
                {
                    skippedCount++;
                    continue;
                }

                var targetFile = Path.Combine(targetFolder, entry.Name);
                using var entryStream = entry.Open();
                using var fs = new FileStream(targetFile, FileMode.Create);
                await entryStream.CopyToAsync(fs);
            }

            return Json(new { 
                success = true, 
                message = "File ZIP berhasil diekstrak.", 
                spisCount, 
                sppsCount, 
                skippedCount 
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Gagal mengekstrak zip: {ex.Message}" });
        }
    }

    [HttpGet]
    public IActionResult GetStdPackingList()
    {
        var stdPackingPath = Path.Combine(_env.WebRootPath, "images", "std-packing");
        var spisPath = Path.Combine(stdPackingPath, "spis");
        var sppsPath = Path.Combine(stdPackingPath, "spps");

        if (!Directory.Exists(spisPath)) Directory.CreateDirectory(spisPath);
        if (!Directory.Exists(sppsPath)) Directory.CreateDirectory(sppsPath);

        var spisFiles = Directory.GetFiles(spisPath).Select(f => Path.GetFileName(f));
        var sppsFiles = Directory.GetFiles(sppsPath).Select(f => Path.GetFileName(f));

        var allItems = new Dictionary<string, (string? spis, string? spps)>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in spisFiles)
        {
            var itemCode = Path.GetFileNameWithoutExtension(file);
            allItems[itemCode] = ($"/images/std-packing/spis/{file}", null);
        }

        foreach (var file in sppsFiles)
        {
            var itemCode = Path.GetFileNameWithoutExtension(file);
            if (allItems.TryGetValue(itemCode, out var val))
            {
                allItems[itemCode] = (val.spis, $"/images/std-packing/spps/{file}");
            }
            else
            {
                allItems[itemCode] = (null, $"/images/std-packing/spps/{file}");
            }
        }

        var result = allItems.Select(kvp => new {
            itemCode = kvp.Key,
            spis = kvp.Value.spis,
            spps = kvp.Value.spps
        }).ToList();

        return Json(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteStdPacking(string itemCode)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            return Json(new { success = false, message = "Item Code kosong." });

        var stdPackingPath = Path.Combine(_env.WebRootPath, "images", "std-packing");
        var spisPath = Path.Combine(stdPackingPath, "spis");
        var sppsPath = Path.Combine(stdPackingPath, "spps");

        var validExtensions = new[] { ".jpg", ".jpeg", ".png" };
        int deletedCount = 0;

        foreach (var ext in validExtensions)
        {
            var fSpis = Path.Combine(spisPath, $"{itemCode}{ext}");
            if (System.IO.File.Exists(fSpis)) { System.IO.File.Delete(fSpis); deletedCount++; }

            var fSpps = Path.Combine(sppsPath, $"{itemCode}{ext}");
            if (System.IO.File.Exists(fSpps)) { System.IO.File.Delete(fSpps); deletedCount++; }
        }

        if (deletedCount > 0)
            return Json(new { success = true, message = "Gambar STD Packing berhasil dihapus." });
        else
            return Json(new { success = false, message = "File gambar tidak ditemukan." });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadSingleStdPacking(string itemCode, string type, IFormFile file)
    {
        if (string.IsNullOrWhiteSpace(itemCode))
            return Json(new { success = false, message = "Item Code kosong." });
        
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "File tidak valid." });

        var ext = Path.GetExtension(file.FileName).ToLower();
        var validExtensions = new[] { ".jpg", ".jpeg", ".png" };
        if (!validExtensions.Contains(ext))
            return Json(new { success = false, message = "Hanya file .jpg, .jpeg, .png yang diizinkan." });

        var stdPackingPath = Path.Combine(_env.WebRootPath, "images", "std-packing");
        var targetFolder = type.Equals("SPIS", StringComparison.OrdinalIgnoreCase) 
            ? Path.Combine(stdPackingPath, "spis") 
            : Path.Combine(stdPackingPath, "spps");

        if (!Directory.Exists(targetFolder)) Directory.CreateDirectory(targetFolder);

        // Delete existing file of any extension for this item code in this folder
        foreach (var existingExt in validExtensions)
        {
            var existingFile = Path.Combine(targetFolder, $"{itemCode}{existingExt}");
            if (System.IO.File.Exists(existingFile)) System.IO.File.Delete(existingFile);
        }

        var targetFile = Path.Combine(targetFolder, $"{itemCode}{ext}");
        using (var stream = new FileStream(targetFile, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        return Json(new { success = true, message = $"Gambar {type.ToUpper()} berhasil diupload." });
    }

    // ============ MAN POWER ============
    public async Task<IActionResult> ManPower()
    {
        SetUser();
        var manPowers = (await _uow.ManPowers.GetAllAsync())
            .Where(m => !m.IsDeleted)
            .OrderBy(m => m.Name)
            .ToList();
        return View(manPowers);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveManPower([FromForm] int Id, [FromForm] string NPK, [FromForm] string Name, [FromForm] string Status)
    {
        try
        {
            if (Id == 0)
            {
                var entity = new SistemPacking.Web.Models.ManPower
                {
                    NPK = NPK,
                    Name = Name,
                    IsActive = Status == "Active",
                    CreatedAt = DateTime.Now
                };
                await _uow.ManPowers.AddAsync(entity);
            }
            else
            {
                var entity = await _uow.ManPowers.GetByIdAsync(Id);
                if (entity == null) return Json(new { success = false, message = "Data tidak ditemukan" });
                entity.NPK = NPK;
                entity.Name = Name;
                entity.IsActive = Status == "Active";
                entity.UpdatedAt = DateTime.Now;
                await _uow.ManPowers.UpdateAsync(entity);
            }
            await _uow.SaveChangesAsync();
            return Json(new { success = true, message = "Data berhasil disimpan" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteManPower([FromForm] int id)
    {
        try
        {
            var entity = await _uow.ManPowers.GetByIdAsync(id);
            if (entity == null) return Json(new { success = false, message = "Data tidak ditemukan" });
            entity.IsDeleted = true;
            entity.UpdatedAt = DateTime.Now;
            await _uow.ManPowers.UpdateAsync(entity);
            await _uow.SaveChangesAsync();
            return Json(new { success = true, message = "Data berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public async Task<IActionResult> ExportManPower()
    {
        var fileContent = await _importExportService.ExportManPowerAsync();
        return File(fileContent, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"ManPower_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    [HttpGet]
    public IActionResult DownloadTemplateManPower()
    {
        var fileContent = _importExportService.GenerateManPowerTemplate();
        return File(fileContent, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_ManPower.xlsx");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportManPower(IFormFile file)
    {
        if (file == null || file.Length <= 0) return Json(new { success = false, message = "File tidak valid" });
        if (!Path.GetExtension(file.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) return Json(new { success = false, message = "Format file harus .xlsx" });

        using var stream = file.OpenReadStream();
        var result = await _importExportService.ImportManPowerAsync(stream);
        return Json(result);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDeleteManPower([FromBody] int[] ids)
    {
        try
        {
            if (ids == null || ids.Length == 0) return Json(new { success = false, message = "Tidak ada data yang dipilih" });
            foreach (var id in ids)
            {
                var entity = await _uow.ManPowers.GetByIdAsync(id);
                if (entity != null)
                {
                    entity.IsDeleted = true;
                    entity.UpdatedAt = DateTime.Now;
                    await _uow.ManPowers.UpdateAsync(entity);
                }
            }
            await _uow.SaveChangesAsync();
            return Json(new { success = true, message = $"{ids.Length} data berhasil dihapus" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}

public class SaveUserRequest
{
    public int Id { get; set; }
    public string NIK { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public bool IsActive { get; set; } = true;
    public string? Email { get; set; }
    public string? Password { get; set; }
}
