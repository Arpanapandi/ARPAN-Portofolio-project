using SistemPacking.Web.Helpers;
using SistemPacking.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SistemPacking.Web.Services;
using SistemPacking.Web.Extensions;
using SistemPacking.Web.Filters;
using SistemPacking.Web.Hubs;
using SistemPacking.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace SistemPacking.Web.Controllers;

[CustomAuthorize("Shopping")]
public class ShoppingController : Controller
{
    private readonly IShoppingService _shoppingService;
    private readonly IOrderService _orderService;
    private readonly IHubContext<OrderHub> _orderHub;
    private readonly SistemPacking.Web.Interfaces.IUnitOfWork _uow;
    private readonly IWebHostEnvironment _env;
    private readonly ApplicationDbContext _db;

    public ShoppingController(IShoppingService shoppingService, IOrderService orderService, IHubContext<OrderHub> orderHub, SistemPacking.Web.Interfaces.IUnitOfWork uow, IWebHostEnvironment env, ApplicationDbContext db)
    {
        _shoppingService = shoppingService;
        _orderService = orderService;
        _orderHub = orderHub;
        _uow = uow;
        _env = env;
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var user = HttpContext.Session.GetUserSession()!;
        ViewBag.User = user;
        // Get orders in WaitingShopping or Shopping status
        var waitingOrders = await _orderService.GetOrdersByStatusAsync(OrderStatus.WaitingShopping);
        var shoppingOrders = await _orderService.GetOrdersByStatusAsync(OrderStatus.Shopping);
        ViewBag.Orders = waitingOrders.Concat(shoppingOrders).ToList();
        ViewBag.Shifts = await _uow.Shifts.GetAllAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scan(string barcode, int? shiftId = null)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return Json(new { success = false, message = "Barcode tidak boleh kosong." });

        var user = HttpContext.Session.GetUserSession()!;
        var result = await _shoppingService.ScanBarcodeAsync(barcode.Trim(), user.Id, shiftId);

        if (result.Success)
        {
            // Resolve STD Packing Images
            string[] extensions = { ".jpg", ".jpeg", ".png" };
            string stdPackingPath = Path.Combine(_env.WebRootPath, "images", "std-packing");
            
            result.SpisImagePath = null;
            result.SppsImagePath = null;
            
            foreach (var ext in extensions)
            {
                if (result.SpisImagePath == null && System.IO.File.Exists(Path.Combine(stdPackingPath, "spis", $"{result.ItemCode}{ext}")))
                    result.SpisImagePath = $"/images/std-packing/spis/{result.ItemCode}{ext}";
                
                if (result.SppsImagePath == null && System.IO.File.Exists(Path.Combine(stdPackingPath, "spps", $"{result.ItemCode}{ext}")))
                    result.SppsImagePath = $"/images/std-packing/spps/{result.ItemCode}{ext}";
            }

            // Broadcast to SignalR group
            await OrderHub.NotifyShoppingLog(_orderHub, result.OrderId, new
            {
                barcode,
                itemName = result.ItemName,
                qty = 1,
                scannedQty = result.ScannedQty,
                targetQty = result.TargetQty,
                isComplete = result.IsComplete,
                operatorName = user.FullName,
                time = DateTime.Now.ToString("HH:mm:ss")
            });
        }

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> GetLogs(int orderId)
    {
        var logs = await _shoppingService.GetShoppingLogsAsync(orderId);
        return Json(logs.Select(l => new
        {
            id = l.Id,
            barcode = l.ScannedBarcode,
            qty = l.ScannedQty,
            isValid = l.IsValid,
            invalidReason = l.InvalidReason,
            operatorName = l.Operator?.FullName ?? "",
            scannedAt = l.ScannedAt.ToString("dd/MM/yyyy HH:mm:ss")
        }));
    }

    [HttpGet]
    public IActionResult Dashboard()
    {
        var user = HttpContext.Session.GetUserSession()!;
        ViewBag.User = user;
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetDashboardLogs()
    {
        var logs = await _db.ShoppingLogs
            .Include(l => l.OrderDetail)
                .ThenInclude(od => od.Item)
            .Include(l => l.Operator)
            .OrderByDescending(l => l.ScannedAt)
            .Take(1000)
            .Select(l => new
            {
                vin = l.OrderDetail.Item.VIN,
                label = l.OrderDetail.Item.ItemCode,
                kanban = l.ScannedBarcode,
                jamScan = l.ScannedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                user = l.Operator.FullName
            })
            .ToListAsync();

        return Json(new { data = logs });
    }

    [HttpGet]
    public async Task<IActionResult> ValidateTagRak(string tagRak)
    {
        if (string.IsNullOrWhiteSpace(tagRak))
            return Json(new { success = false, message = "Barcode Rak tidak boleh kosong." });

        tagRak = tagRak.Trim();
        // Validation: Must end with 'X' or 'x'
        if (!tagRak.EndsWith("X", StringComparison.OrdinalIgnoreCase))
            return Json(new { success = false, message = "Format Barcode Rak tidak valid (harus diakhiri X)." });

        string vin = tagRak.Substring(0, tagRak.Length - 1);
        
        // Find if this VIN exists in items
        var item = await _db.Items.IgnoreQueryFilters()
                    .Where(i => i.VIN == vin && !i.IsDeleted)
                    .FirstOrDefaultAsync();

        if (item == null)
            return Json(new { success = false, message = $"Data Master Item dengan VIN '{vin}' tidak ditemukan." });

        return Json(new { success = true, vin = item.VIN, itemName = item.ItemName });
    }
}

[CustomAuthorize("Packing")]
public class Packing_SPController : Controller
{
    private readonly IPackingService _packingService;
    private readonly IOrderService _orderService;
    private readonly IHubContext<OrderHub> _orderHub;
    private readonly IHubContext<DashboardHub> _dashboardHub;
    private readonly IDashboardService _dashboardService;
    private readonly SistemPacking.Web.Interfaces.IUnitOfWork _uow;
    private readonly IWebHostEnvironment _env;

    public Packing_SPController(IPackingService packingService, IOrderService orderService, IHubContext<OrderHub> orderHub, IHubContext<DashboardHub> dashboardHub, IDashboardService dashboardService, SistemPacking.Web.Interfaces.IUnitOfWork uow, IWebHostEnvironment env)
    {
        _packingService = packingService;
        _orderService = orderService;
        _orderHub = orderHub;
        _dashboardHub = dashboardHub;
        _dashboardService = dashboardService;
        _uow = uow;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var user = HttpContext.Session.GetUserSession()!;
        ViewBag.User = user;
        var readyOrders = await _orderService.GetOrdersByStatusAsync(OrderStatus.ReadyPacking);
        var packingOrders = await _orderService.GetOrdersByStatusAsync(OrderStatus.Packing);
        ViewBag.Orders = readyOrders.Concat(packingOrders).ToList();
        ViewBag.Shifts = await _uow.Shifts.GetAllAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scan(string kanban, string label, int? shiftId = null)
    {
        if (string.IsNullOrWhiteSpace(kanban) || string.IsNullOrWhiteSpace(label))
            return Json(new { success = false, message = "Kanban dan Label tidak boleh kosong." });

        var user = HttpContext.Session.GetUserSession()!;
        var result = await _packingService.ScanKanbanLabelAsync(kanban.Trim(), label.Trim(), user.Id, shiftId, "SparePart");

        if (result.Success)
        {
            // Resolve STD Packing Images
            string[] extensions = { ".jpg", ".jpeg", ".png" };
            string stdPackingPath = Path.Combine(_env.WebRootPath, "images", "std-packing");
            
            result.SpisImagePath = null;
            result.SppsImagePath = null;
            
            foreach (var ext in extensions)
            {
                if (result.SpisImagePath == null && System.IO.File.Exists(Path.Combine(stdPackingPath, "spis", $"{result.ItemCode}{ext}")))
                    result.SpisImagePath = $"/images/std-packing/spis/{result.ItemCode}{ext}";
                
                if (result.SppsImagePath == null && System.IO.File.Exists(Path.Combine(stdPackingPath, "spps", $"{result.ItemCode}{ext}")))
                    result.SppsImagePath = $"/images/std-packing/spps/{result.ItemCode}{ext}";
            }

            // Get updated progress
            var progress = await _packingService.GetPackingProgressAsync(result.OrderId);

            // Broadcast progress update via SignalR
            await OrderHub.NotifyPackingProgress(_orderHub, result.OrderId,
                progress?.PackingProgress ?? 0,
                result.OrderDetailId ?? 0,
                result.ActualQty,
                result.TargetQty);

            // Refresh dashboard KPI
            var kpi = await _dashboardService.GetDashboardKpiAsync();
            await DashboardHub.NotifyKpiUpdated(_dashboardHub, kpi);
        }

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> Progress(int orderId)
    {
        var progress = await _packingService.GetPackingProgressAsync(orderId);
        if (progress == null) return NotFound();
        return Json(progress);
    }

    [HttpGet]
    public async Task<IActionResult> Detail(int orderId)
    {
        var user = HttpContext.Session.GetUserSession()!;
        ViewBag.User = user;
        var progress = await _packingService.GetPackingProgressAsync(orderId);
        if (progress == null) return NotFound();
        return View(progress);
    }
}

[CustomAuthorize("Packing")]
public class Packing_ExController : Controller
{
    private readonly IPackingService _packingService;
    private readonly IOrderService _orderService;
    private readonly IHubContext<OrderHub> _orderHub;
    private readonly IHubContext<DashboardHub> _dashboardHub;
    private readonly IDashboardService _dashboardService;
    private readonly SistemPacking.Web.Interfaces.IUnitOfWork _uow;
    private readonly IWebHostEnvironment _env;

    public Packing_ExController(IPackingService packingService, IOrderService orderService, IHubContext<OrderHub> orderHub, IHubContext<DashboardHub> dashboardHub, IDashboardService dashboardService, SistemPacking.Web.Interfaces.IUnitOfWork uow, IWebHostEnvironment env)
    {
        _packingService = packingService;
        _orderService = orderService;
        _orderHub = orderHub;
        _dashboardHub = dashboardHub;
        _dashboardService = dashboardService;
        _uow = uow;
        _env = env;
    }

    public async Task<IActionResult> Index()
    {
        var user = HttpContext.Session.GetUserSession()!;
        ViewBag.User = user;
        var readyOrders = await _orderService.GetOrdersByStatusAsync(OrderStatus.ReadyPacking);
        var packingOrders = await _orderService.GetOrdersByStatusAsync(OrderStatus.Packing);
        ViewBag.Orders = readyOrders.Concat(packingOrders).ToList();
        ViewBag.Shifts = await _uow.Shifts.GetAllAsync();
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Scan(string kanban, string label, int? shiftId = null)
    {
        if (string.IsNullOrWhiteSpace(kanban) || string.IsNullOrWhiteSpace(label))
            return Json(new { success = false, message = "Kanban dan Label tidak boleh kosong." });

        var user = HttpContext.Session.GetUserSession()!;
        var result = await _packingService.ScanKanbanLabelAsync(kanban.Trim(), label.Trim(), user.Id, shiftId, "Export");

        if (result.Success)
        {
            // Resolve STD Packing Images
            string[] extensions = { ".jpg", ".jpeg", ".png" };
            string stdPackingPath = Path.Combine(_env.WebRootPath, "images", "std-packing");
            
            result.SpisImagePath = null;
            result.SppsImagePath = null;
            
            foreach (var ext in extensions)
            {
                if (result.SpisImagePath == null && System.IO.File.Exists(Path.Combine(stdPackingPath, "spis", $"{result.ItemCode}{ext}")))
                    result.SpisImagePath = $"/images/std-packing/spis/{result.ItemCode}{ext}";
                
                if (result.SppsImagePath == null && System.IO.File.Exists(Path.Combine(stdPackingPath, "spps", $"{result.ItemCode}{ext}")))
                    result.SppsImagePath = $"/images/std-packing/spps/{result.ItemCode}{ext}";
            }

            // Get updated progress
            var progress = await _packingService.GetPackingProgressAsync(result.OrderId);

            // Broadcast progress update via SignalR
            await OrderHub.NotifyPackingProgress(_orderHub, result.OrderId,
                progress?.PackingProgress ?? 0,
                result.OrderDetailId ?? 0,
                result.ActualQty,
                result.TargetQty);

            // Refresh dashboard KPI
            var kpi = await _dashboardService.GetDashboardKpiAsync();
            await DashboardHub.NotifyKpiUpdated(_dashboardHub, kpi);
        }

        return Json(result);
    }

    [HttpGet]
    public async Task<IActionResult> Progress(int orderId)
    {
        var progress = await _packingService.GetPackingProgressAsync(orderId);
        if (progress == null) return NotFound();
        return Json(progress);
    }

    [HttpGet]
    public async Task<IActionResult> Detail(int orderId)
    {
        var user = HttpContext.Session.GetUserSession()!;
        ViewBag.User = user;
        var progress = await _packingService.GetPackingProgressAsync(orderId);
        if (progress == null) return NotFound();
        return View(progress);
    }
}
