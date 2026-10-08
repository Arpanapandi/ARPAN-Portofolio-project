using SistemPacking.Web.Helpers;
using SistemPacking.Web.Models;
using Microsoft.AspNetCore.Mvc;
using SistemPacking.Web.Extensions;
using SistemPacking.Web.Filters;

namespace SistemPacking.Web.Controllers;

[CustomAuthorize("ViewReport")]
public class ReportController : Controller
{
    private void SetUser() => ViewBag.User = HttpContext.Session.GetUserSession();

    public IActionResult Daily() { SetUser(); return View(); }
    public IActionResult Monthly() { SetUser(); return View(); }
    public IActionResult Shopping() { SetUser(); return View(); }
    public IActionResult Packing() { SetUser(); return View(); }
    public IActionResult Verification() { SetUser(); return View(); }
    public IActionResult Productivity() { SetUser(); return View(); }
}
