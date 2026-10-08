using SistemPacking.Web.Helpers;
using SistemPacking.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SistemPacking.Web.Services;
using SistemPacking.Web.Extensions;
using SistemPacking.Web.Filters;
using SistemPacking.Web.Hubs;

namespace SistemPacking.Web.Controllers;

[CustomAuthorize]
public class DashboardController : Controller
{
    private readonly IDashboardService _dashboardService;
    private readonly IShippingDashboardService _shippingDashboardService;
    private readonly IHubContext<DashboardHub> _hubContext;

    public DashboardController(
        IDashboardService dashboardService,
        IShippingDashboardService shippingDashboardService,
        IHubContext<DashboardHub> hubContext)
    {
        _dashboardService = dashboardService;
        _shippingDashboardService = shippingDashboardService;
        _hubContext = hubContext;
    }

    // Keep old Index redirect to Shipping
    public IActionResult Index()
    {
        return RedirectToAction("Shipping");
    }

    public async Task<IActionResult> Shipping()
    {
        var user = HttpContext.Session.GetUserSession()!;
        ViewBag.User = user;
        var kpi = await _shippingDashboardService.GetShippingKpiAsync();
        return View(kpi);
    }

    public IActionResult Stock([FromServices] SistemPacking.Web.Data.ApplicationDbContext dbContext)
    {
        var user = HttpContext.Session.GetUserSession()!;
        ViewBag.User = user;
        var items = dbContext.Items.Where(i => i.ActQty > 0).OrderBy(i => i.ItemCode).ToList();
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> GetStockTableData([FromServices] SistemPacking.Web.Data.ApplicationDbContext dbContext)
    {
        var items = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            dbContext.Items
            .Where(i => i.ActQty > 0)
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
                max = i.Max,
                category = i.Category ?? "Unknown"
            })
        );
        return Json(new { success = true, data = items });
    }

    [HttpGet]
    public async Task<IActionResult> LookupItems(string term, [FromServices] SistemPacking.Web.Data.ApplicationDbContext dbContext)
    {
        if (string.IsNullOrEmpty(term)) return Json(new List<object>());

        var items = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            dbContext.Items
            .Where(i => (i.VIN != null && i.VIN.Contains(term)) || i.ItemCode.Contains(term) || i.ItemName.Contains(term))
            .Take(20)
            .Select(i => new {
                id = i.Id,
                vin = i.VIN ?? "-",
                itemCode = i.ItemCode,
                itemName = i.ItemName,
                category = i.Category ?? "Unknown",
                actQty = i.ActQty
            })
        );
        return Json(items);
    }

    [HttpPost]
    public async Task<IActionResult> AdjustStock([FromBody] AdjustStockRequest req, [FromServices] SistemPacking.Web.Data.ApplicationDbContext dbContext)
    {
        if (req == null || req.ItemId <= 0 || req.Qty == 0)
        {
            return Json(new { success = false, message = "Data tidak valid" });
        }

        var item = await dbContext.Items.FindAsync(req.ItemId);
        if (item == null)
        {
            return Json(new { success = false, message = "Item tidak ditemukan" });
        }

        item.ActQty += req.Qty;
        // Optional: Ensure ActQty doesn't go below zero if it's not allowed
        if (item.ActQty < 0) item.ActQty = 0;
        
        dbContext.Items.Update(item);
        await dbContext.SaveChangesAsync();

        return Json(new { success = true, message = "Stock berhasil diupdate" });
    }

    [HttpGet]
    public async Task<IActionResult> GetShippingKpi()
    {
        var kpi = await _shippingDashboardService.GetShippingKpiAsync();
        return Json(kpi);
    }

    [HttpGet]
    public async Task<IActionResult> GetTableData(string indicator)
    {
        object data = indicator switch
        {
            "ProgresShopping" => await _shippingDashboardService.GetShoppingProgressDataAsync(),
            "ProgresPacking"  => await _shippingDashboardService.GetPackingProgressDataAsync(),
            "ProgresDelivery" => await _shippingDashboardService.GetDeliveryProgressDataAsync(),
            "DelayShopping"   => await _shippingDashboardService.GetDelayShoppingDataAsync(),
            "DelayPacking"    => await _shippingDashboardService.GetDelayPackingDataAsync(),
            "DelayDelivery"   => await _shippingDashboardService.GetDelayDeliveryDataAsync(),
            "Finished"        => await _shippingDashboardService.GetFinishedDataAsync(),
            _ => new List<object>()
        };
        return Json(data);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BulkDelete([FromBody] BulkDeleteRequest request)
    {
        if (request?.Ids == null || request.Ids.Length == 0)
            return Json(new { success = false, message = "Tidak ada data yang dipilih." });

        // Detail-level delete for Shopping/Packing indicators
        if (request.Type == "detail")
        {
            var (success, message) = await _shippingDashboardService.BulkDeleteOrderDetailsAsync(request.Ids);
            return Json(new { success, message });
        }
        else
        {
            var (success, message) = await _shippingDashboardService.BulkDeleteOrdersAsync(request.Ids);
            return Json(new { success, message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateDelivery([FromBody] UpdateDeliveryRequest request)
    {
        if (request == null)
            return Json(new { success = false, message = "Data tidak valid." });

        var (success, message) = await _shippingDashboardService.UpdateDeliveryAsync(
            request.OrderId,
            request.PlanDeliveryDate,
            request.ActualDeliveryDate,
            request.PlanETD,
            request.ActualETD,
            request.DeliveryRemark);
        return Json(new { success, message });
    }

    // Legacy endpoints kept for backwards compat
    [HttpGet]
    public async Task<IActionResult> GetKpi()
    {
        var kpi = await _dashboardService.GetDashboardKpiAsync();
        return Json(kpi);
    }

    [HttpGet]
    public async Task<IActionResult> GetOrderSummaries()
    {
        var summaries = await _dashboardService.GetOrderSummariesAsync();
        return Json(summaries);
    }
}

public class BulkDeleteRequest
{
    public int[] Ids { get; set; } = Array.Empty<int>();
    public string Type { get; set; } = "detail"; // "detail" or "order"
}

public class UpdateDeliveryRequest
{
    public int OrderId { get; set; }
    public DateTime? PlanDeliveryDate { get; set; }
    public DateTime? ActualDeliveryDate { get; set; }
    public string? PlanETD { get; set; }
    public string? ActualETD { get; set; }
    public string? DeliveryRemark { get; set; }
}

public class AdjustStockRequest { public int ItemId { get; set; } public decimal Qty { get; set; } }
