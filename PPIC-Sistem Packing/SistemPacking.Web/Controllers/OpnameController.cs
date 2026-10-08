using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SistemPacking.Web.Models;
using SistemPacking.Web.Extensions;
using SistemPacking.Web.Filters;
using SistemPacking.Web.Hubs;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace SistemPacking.Web.Controllers;

[CustomAuthorize]
public class OpnameController : Controller
{
    private readonly SistemPacking.Web.Data.ApplicationDbContext _context;
    private readonly IHubContext<DashboardHub> _hubContext;

    public OpnameController(
        SistemPacking.Web.Data.ApplicationDbContext context,
        IHubContext<DashboardHub> hubContext)
    {
        _context = context;
        _hubContext = hubContext;
    }

    public IActionResult Index()
    {
        var user = HttpContext.Session.GetUserSession()!;
        ViewBag.User = user;
        
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetStockData()
    {
        var items = await _context.Items
            .OrderBy(i => i.ItemCode)
            .Select(i => new {
                id = i.Id,
                vin = i.VIN ?? "",
                itemCode = i.ItemCode,
                itemName = i.ItemName,
                dock = i.Dock ?? "",
                prodPlant = i.ProdPlant ?? "",
                lokasiRack = i.LokasiRack ?? "",
                rack = i.Rack ?? "",
                noRack = i.NoRack ?? "",
                qtyPcs = i.QtyPcs,
                actQty = i.ActQty,
                min = i.Min,
                rop = i.Rop,
                max = i.Max
            })
            .ToListAsync();

        return Json(new { success = true, data = items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> GetItemsByIds([FromBody] int[] ids)
    {
        if (ids == null || ids.Length == 0)
        {
            return Json(new { success = false, message = "Tidak ada item yang dipilih", data = new List<object>() });
        }

        var items = await _context.Items
            .Where(i => ids.Contains(i.Id))
            .OrderBy(i => i.ItemCode)
            .Select(i => new {
                id = i.Id,
                vin = i.VIN ?? "",
                itemCode = i.ItemCode,
                itemName = i.ItemName,
                dock = i.Dock ?? "",
                prodPlant = i.ProdPlant ?? "",
                lokasiRack = i.LokasiRack ?? "",
                rack = i.Rack ?? "",
                noRack = i.NoRack ?? "",
                qtyPcs = i.QtyPcs,
                actQty = i.ActQty,
                min = i.Min,
                rop = i.Rop,
                max = i.Max
            })
            .ToListAsync();

        return Json(new { success = true, data = items });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveActual([FromBody] OpnameUpdateRequest request)
    {
        if (request == null || request.Id <= 0)
        {
            return Json(new { success = false, message = "Data tidak valid" });
        }

        var item = await _context.Items.FindAsync(request.Id);
        if (item == null)
        {
            return Json(new { success = false, message = "Item tidak ditemukan" });
        }

        item.ActQty = request.ActQty;
        await _context.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("StockUpdated");

        return Json(new { success = true, message = "Stock aktual berhasil diperbarui" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveActualBulk([FromBody] OpnameBulkUpdateRequest request)
    {
        if (request?.Items == null || request.Items.Count == 0)
        {
            return Json(new { success = false, message = "Tidak ada data untuk disimpan" });
        }

        var itemIds = request.Items.Select(x => x.Id).ToList();
        var dbItems = await _context.Items.Where(i => itemIds.Contains(i.Id)).ToListAsync();
        var dict = request.Items.ToDictionary(x => x.Id, x => x.ActQty);

        foreach (var dbItem in dbItems)
        {
            if (dict.TryGetValue(dbItem.Id, out var newActQty))
            {
                dbItem.ActQty = newActQty;
            }
        }

        await _context.SaveChangesAsync();

        await _hubContext.Clients.All.SendAsync("StockUpdated");

        return Json(new { success = true, message = $"{dbItems.Count} data stock aktual berhasil disimpan" });
    }
}

public class OpnameUpdateRequest
{
    public int Id { get; set; }
    public decimal ActQty { get; set; }
}

public class OpnameBulkUpdateRequest
{
    public List<OpnameUpdateRequest> Items { get; set; } = new();
}
