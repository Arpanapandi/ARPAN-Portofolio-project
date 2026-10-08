using SistemPacking.Web.Helpers;
using SistemPacking.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SistemPacking.Web.Data;
using SistemPacking.Web.Extensions;
using SistemPacking.Web.Filters;
using System.Linq;
using System.Threading.Tasks;

namespace SistemPacking.Web.Controllers;

[CustomAuthorize("ViewReport")]
public class TransactionLogController : Controller
{
    private readonly ApplicationDbContext _db;

    public TransactionLogController(ApplicationDbContext db)
    {
        _db = db;
    }

    private void SetUserSession() => ViewBag.User = HttpContext.Session.GetUserSession();

    public IActionResult Shopping()
    {
        SetUserSession();
        return View();
    }

    public IActionResult PackingExport()
    {
        SetUserSession();
        return View();
    }

    public IActionResult PackingSparePart()
    {
        SetUserSession();
        return View("PackingServicePart");
    }

    public IActionResult PackingServicePart()
    {
        SetUserSession();
        return View("PackingServicePart");
    }

    public IActionResult ScanNgShopping()
    {
        SetUserSession();
        return View();
    }

    public IActionResult ScanNgPacking()
    {
        SetUserSession();
        return View();
    }

    [HttpGet]
    public async Task<IActionResult> GetShoppingLogs()
    {
        var logs = await _db.ShoppingLogs
            .Include(l => l.Item)
            .Include(l => l.Operator)
            .OrderByDescending(l => l.ScannedAt)
            .Take(1000)
            .Select(l => new
            {
                waktu = l.ScannedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                vin = l.Item != null ? l.Item.VIN ?? "-" : "-",
                label = l.ScannedBarcode,
                kanban = l.Item != null ? l.Item.ItemCode : "-",
                qty = l.ScannedQty,
                operatorName = l.Operator != null ? (l.Operator.FullName ?? l.Operator.Username) : "Unknown",
                isValid = l.IsValid,
                invalidReason = l.InvalidReason
            })
            .ToListAsync();

        return Json(new { data = logs });
    }

    [HttpGet]
    public async Task<IActionResult> GetPackingExportLogs()
    {
        var logs = await _db.PackingLogViews
            .Where(l => l.Category == "Export")
            .OrderByDescending(l => l.PackedAt)
            .Take(1000)
            .Select(l => new
            {
                waktu = l.PackedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                orderNo = l.OrderNo,
                item = l.ItemName,
                barcode = l.ScannedBarcode,
                qty = l.PackedQty,
                operatorName = l.OperatorName
            })
            .ToListAsync();

        return Json(new { data = logs });
    }

    [HttpGet]
    public async Task<IActionResult> GetPackingSparePartLogs()
    {
        var logs = await _db.PackingLogViews
            .Where(l => l.Category == "SparePart")
            .OrderByDescending(l => l.PackedAt)
            .Take(1000)
            .Select(l => new
            {
                waktu = l.PackedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                orderNo = l.OrderNo,
                item = l.ItemName,
                barcode = l.ScannedBarcode,
                qty = l.PackedQty,
                operatorName = l.OperatorName
            })
            .ToListAsync();

        return Json(new { data = logs });
    }

    [HttpGet]
    public async Task<IActionResult> GetScanNgLogs(string category)
    {
        var logs = await _db.ScanNgLogs
            .Include(l => l.Operator)
            .Where(l => l.Category == category || l.Module == category)
            .OrderByDescending(l => l.ScannedAt)
            .Take(1000)
            .Select(l => new
            {
                waktu = l.ScannedAt.ToString("dd/MM/yyyy HH:mm:ss"),
                module = l.Module,
                category = l.Category ?? "-",
                barcode = l.ScannedBarcode,
                kanban = l.ScannedBarcode,
                itemCode = l.ItemCode ?? "-",
                label = l.ItemCode ?? "-",
                alasan = l.ErrorMessage,
                errorMessage = l.ErrorMessage,
                operatorName = l.Operator.FullName
            })
            .ToListAsync();

        return Json(new { data = logs });
    }
}
