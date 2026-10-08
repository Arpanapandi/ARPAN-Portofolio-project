using SistemPacking.Web.Helpers;
using SistemPacking.Web.Models;
using Microsoft.AspNetCore.Mvc;
using SistemPacking.Web.Services;
using SistemPacking.Web.Interfaces;
using SistemPacking.Web.Extensions;
using SistemPacking.Web.Filters;

namespace SistemPacking.Web.Controllers;

[CustomAuthorize("Verify")]
public class VerificationController : Controller
{
    private readonly IVerificationService _verificationService;
    private readonly IOrderService _orderService;
    private readonly IUnitOfWork _uow;

    public VerificationController(IVerificationService verificationService,
        IOrderService orderService, IUnitOfWork uow)
    {
        _verificationService = verificationService;
        _orderService = orderService;
        _uow = uow;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.User = HttpContext.Session.GetUserSession()!;
        var pending = await _verificationService.GetPendingVerificationsAsync();
        // Also get packing complete orders waiting to be submitted
        var packingComplete = await _orderService.GetOrdersByStatusAsync(OrderStatus.PackingComplete);
        ViewBag.PackingCompleteOrders = packingComplete;
        return View(pending);
    }

    public async Task<IActionResult> Detail(int orderId)
    {
        ViewBag.User = HttpContext.Session.GetUserSession()!;
        var dto = await _verificationService.GetVerificationAsync(orderId);
        if (dto == null) return NotFound();

        var reasons = await _uow.ReasonRejects.GetAllAsync();
        ViewBag.RejectReasons = reasons;
        return View(dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Submit(int orderId)
    {
        var user = HttpContext.Session.GetUserSession()!;
        var (success, message) = await _verificationService.SubmitForVerificationAsync(orderId, user.Id);
        return Json(new { success, message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int orderId, string? notes)
    {
        var user = HttpContext.Session.GetUserSession()!;
        var (success, message) = await _verificationService.ApproveAsync(orderId, user.Id, notes);
        return Json(new { success, message });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int orderId, string notes, int? reasonId)
    {
        var user = HttpContext.Session.GetUserSession()!;
        var (success, message) = await _verificationService.RejectAsync(orderId, user.Id, notes, reasonId);
        return Json(new { success, message });
    }
}
