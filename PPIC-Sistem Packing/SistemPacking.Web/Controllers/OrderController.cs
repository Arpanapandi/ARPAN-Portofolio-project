using SistemPacking.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using SistemPacking.Web.Models;
using SistemPacking.Web.Services;
using SistemPacking.Web.Extensions;
using SistemPacking.Web.Filters;
using SistemPacking.Web.Hubs;

namespace SistemPacking.Web.Controllers;

[CustomAuthorize("ManageOrder")]
public class OrderController : Controller
{
    private readonly IOrderService _orderService;
    private readonly IHubContext<DashboardHub> _dashboardHub;

    public OrderController(IOrderService orderService, IHubContext<DashboardHub> dashboardHub)
    {
        _orderService = orderService;
        _dashboardHub = dashboardHub;
    }

    public async Task<IActionResult> Index()
    {
        var orders = await _orderService.GetAllOrderDetailsFlattenedAsync();
        ViewBag.User = HttpContext.Session.GetUserSession()!;
        return View(orders);
    }

    public async Task<IActionResult> Detail(int id)
    {
        var order = await _orderService.GetOrderAsync(id);
        if (order == null) return NotFound();
        ViewBag.User = HttpContext.Session.GetUserSession()!;
        return View(order);
    }

    [HttpGet]
    public IActionResult ManualInput()
    {
        ViewBag.User = HttpContext.Session.GetUserSession()!;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveManualInput(string OrderDate, string OrderNo, string CustomerName, string PartNo, string VIN, decimal TargetQty)
    {
        try
        {
            // Re-use logic similar to excel upload, just passing as a dummy csv/excel or calling a new service method
            // To save time, we will construct a dummy csv and pass to the existing UploadOrderExcelAsync logic
            using var workbook = new ClosedXML.Excel.XLWorkbook();
            var ws = workbook.Worksheets.Add("Sheet1");
            ws.Cell(1, 1).Value = "Jadwal";
            ws.Cell(1, 2).Value = "OrderNo";
            ws.Cell(1, 3).Value = "Dock";
            ws.Cell(1, 4).Value = "CustomerName";
            ws.Cell(1, 5).Value = "PartNo";
            ws.Cell(1, 6).Value = "VIN";
            ws.Cell(1, 7).Value = "TargetQty";

            ws.Cell(2, 1).Value = OrderDate;
            ws.Cell(2, 2).Value = OrderNo;
            ws.Cell(2, 3).Value = "-";
            ws.Cell(2, 4).Value = CustomerName;
            ws.Cell(2, 5).Value = PartNo;
            ws.Cell(2, 6).Value = VIN;
            ws.Cell(2, 7).Value = TargetQty;

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            stream.Position = 0;

            var user = HttpContext.Session.GetUserSession()!;
            var result = await _orderService.UploadOrderExcelAsync(stream, "Manual_Input.xlsx", user.Id);

            if (result.Success)
            {
                return Json(new { success = true, message = "Data berhasil disimpan." });
            }
            else
            {
                var errMsg = result.Errors?.FirstOrDefault() ?? "Gagal memproses data.";
                return Json(new { success = false, message = errMsg });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    public IActionResult Upload()
    {
        ViewBag.User = HttpContext.Session.GetUserSession()!;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Upload(IFormFile file)
    {
        if (file == null || file.Length == 0)
            return Json(new { success = false, message = "File tidak boleh kosong." });

        var ext = Path.GetExtension(file.FileName).ToLower();
        if (ext != ".xlsx" && ext != ".xls")
            return Json(new { success = false, message = "Format file harus .xlsx atau .xls" });

        var user = HttpContext.Session.GetUserSession()!;
        using var stream = file.OpenReadStream();
        var result = await _orderService.UploadOrderExcelAsync(stream, file.FileName, user.Id);
        return Json(result);
    }

    [HttpGet]
    public IActionResult DownloadTemplate()
    {
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Template Order");

        // Headers
        worksheet.Cell(1, 1).Value = "Jadwal (yyyy-MM-dd)";
        worksheet.Cell(1, 2).Value = "Order No";
        worksheet.Cell(1, 3).Value = "Dock";
        worksheet.Cell(1, 4).Value = "Nama Customer";
        worksheet.Cell(1, 5).Value = "Part No";
        worksheet.Cell(1, 6).Value = "VIN";
        worksheet.Cell(1, 7).Value = "QTY";

        // Styling headers
        var headerRange = worksheet.Range(1, 1, 1, 7);
        headerRange.Style.Font.Bold = true;
        headerRange.Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.LightGray;
        headerRange.Style.Alignment.Horizontal = ClosedXML.Excel.XLAlignmentHorizontalValues.Center;

        // Dummy data row (optional)
        worksheet.Cell(2, 1).Value = DateTime.Now.ToString("yyyy-MM-dd");
        worksheet.Cell(2, 2).Value = "ORD-0001";
        worksheet.Cell(2, 3).Value = "DOCK-1";
        worksheet.Cell(2, 4).Value = "CUSTOMER A";
        worksheet.Cell(2, 5).Value = "PART-123";
        worksheet.Cell(2, 6).Value = "VIN-123";
        worksheet.Cell(2, 7).Value = 100;

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var content = stream.ToArray();
        
        return File(content, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "Template_Upload_Order.xlsx");
    }

    [HttpGet]
    public async Task<IActionResult> History()
    {
        var histories = await _orderService.GetUploadHistoriesAsync();
        ViewBag.User = HttpContext.Session.GetUserSession()!;
        return View(histories);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Release(int id)
    {
        var user = HttpContext.Session.GetUserSession()!;
        var (success, message) = await _orderService.ReleaseOrderAsync(id, user.Id);
        return Json(new { success, message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(int id, string reason)
    {
        var user = HttpContext.Session.GetUserSession()!;
        var (success, message) = await _orderService.CancelOrderAsync(id, user.Id, reason);
        return Json(new { success, message });
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var orders = await _orderService.GetAllOrdersAsync();
        return Json(orders);
    }

    [HttpGet]
    public async Task<IActionResult> GetDetail(int id)
    {
        var order = await _orderService.GetOrderAsync(id);
        if (order == null) return NotFound();
        return Json(order);
    }
}
