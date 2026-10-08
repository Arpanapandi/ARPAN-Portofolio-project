using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Authorization;

namespace DeliveryControl.Filters
{
    public class AuthorizeAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // Skip check for AccountController (Login/Logout)
            var controllerName = context.RouteData?.Values["controller"]?.ToString();
            if (string.Equals(controllerName, "Account", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            
            // PENTING: Cek attribute [AllowAnonymous] di endpoint/action/controller
            if (context.ActionDescriptor.EndpointMetadata.Any(em => em is IAllowAnonymous || em.GetType().Name.Contains("AllowAnonymous")))
            {
                return;
            }

            var session = context.HttpContext.Session;
            if (session == null) 
            {
                 context.Result = new RedirectToActionResult("Login", "Account", null);
                 return;
            }

            var userId = session.GetString("UserId");

            if (string.IsNullOrEmpty(userId))
            {
                // Jika belum login, redirect ke login
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            base.OnActionExecuting(context);
        }
    }

    public class AuthorizeAdminAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var session = context.HttpContext.Session;
            var userId = session.GetString("UserId");
            var role = session.GetString("Role");

            if (string.IsNullOrEmpty(userId))
            {
                // Jika belum login, redirect ke login
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            if (role != "Admin" && role != "Super Admin")
            {
                // Jika bukan admin atau super admin, redirect ke home dengan error message
                context.HttpContext.Session.SetString("ErrorMessage", "Anda tidak memiliki akses untuk halaman ini");
                context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
                return;
            }

            base.OnActionExecuting(context);
        }
    }

    /// <summary>
    /// Filter untuk membatasi akses berdasarkan Role. Gunakan: [AuthorizeRoles("Admin", "Preparation")]
    /// </summary>
    public class AuthorizeRolesAttribute : ActionFilterAttribute
    {
        private readonly string[] _roles;

        public AuthorizeRolesAttribute(params string[] roles)
        {
            _roles = roles;
        }

        public override void OnActionExecuting(ActionExecutingContext context)
        {
            // Cek attribute [AllowAnonymous] di endpoint/action/controller
            if (context.ActionDescriptor.EndpointMetadata.Any(em => em is IAllowAnonymous || em.GetType().Name.Contains("AllowAnonymous")))
            {
                return;
            }

            var session = context.HttpContext.Session;
            var userId = session.GetString("UserId");
            var role   = session.GetString("Role") ?? "";
            var username = (session.GetString("Username") ?? "").ToLower();

            // Jika belum login → ke halaman login
            if (string.IsNullOrEmpty(userId))
            {
                // context.HttpContext.Session.SetString("ErrorMessage", "DEBUG: Redirecting from AuthorizeRoles because userId is null");
                context.Result = new RedirectToActionResult("Login", "Account", null);
                return;
            }

            // Super Admin memiliki akses ke segalanya
            bool isSuperAdmin = string.Equals(role, "Super Admin", StringComparison.OrdinalIgnoreCase);

            // Cek apakah role user ada dalam daftar role yang diizinkan (case-insensitive)
            if (!isSuperAdmin && !_roles.Any(r => string.Equals(r, role, StringComparison.OrdinalIgnoreCase)))
            {
                context.HttpContext.Session.SetString("ErrorMessage", 
                    $"Akses ditolak. Halaman ini hanya untuk role: {string.Join(", ", _roles)}.");
                context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}

