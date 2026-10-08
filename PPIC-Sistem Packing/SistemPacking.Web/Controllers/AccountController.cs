using SistemPacking.Web.Helpers;
using Microsoft.AspNetCore.Mvc;
using SistemPacking.Web.Models;
using SistemPacking.Web.Services;
using SistemPacking.Web.Extensions;

namespace SistemPacking.Web.Controllers;

public class AccountController : Controller
{
    private readonly IAuthService _authService;

    public AccountController(IAuthService authService)
    {
        _authService = authService;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (HttpContext.Session.IsAuthenticated())
            return RedirectToAction("Index", "Dashboard");
        ViewBag.ReturnUrl = returnUrl;
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginDto model, string? returnUrl = null)
    {
        if (!ModelState.IsValid)
            return View(model);

        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var userAgent = Request.Headers["User-Agent"].ToString();

        var (success, message, user) = await _authService.LoginAsync(model, ip, userAgent);

        if (!success)
        {
            ModelState.AddModelError("", message);
            return View(model);
        }

        // Regenerate session ID to prevent session fixation
        HttpContext.Session.Clear();
        HttpContext.Session.SetUserSession(user!);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction("Index", "Dashboard");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        var user = HttpContext.Session.GetUserSession();
        if (user != null)
        {
            var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            await _authService.LogoutAsync(user.Id, ip);
        }
        HttpContext.Session.ClearUserSession();
        return RedirectToAction("Login");
    }

    public IActionResult AccessDenied()
    {
        return View();
    }
}
