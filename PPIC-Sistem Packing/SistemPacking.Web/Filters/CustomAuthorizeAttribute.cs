using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using SistemPacking.Web.Models;
using System.Text.Json;

namespace SistemPacking.Web.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class CustomAuthorizeAttribute : Attribute, IAuthorizationFilter
{
    private readonly string _permission;

    public CustomAuthorizeAttribute(string permission = "")
    {
        _permission = permission;
    }

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var hasAllowAnonymous = context.ActionDescriptor.EndpointMetadata
            .Any(em => em.GetType() == typeof(Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute));
        
        if (hasAllowAnonymous) return;

        var sessionJson = context.HttpContext.Session.GetString("UserSession");
        if (string.IsNullOrEmpty(sessionJson))
        {
            RedirectToLogin(context);
            return;
        }

        UserSessionDto? user;
        try
        {
            user = JsonSerializer.Deserialize<UserSessionDto>(sessionJson);
        }
        catch
        {
            RedirectToLogin(context);
            return;
        }

        if (user == null)
        {
            RedirectToLogin(context);
            return;
        }

        // Check specific permission
        if (!string.IsNullOrEmpty(_permission))
        {
            bool hasPermission = _permission switch
            {
                "ManageMaster" => user.CanManageMaster,
                "ManageOrder" => user.CanManageOrder,
                "Shopping" => user.CanShopping,
                "Packing" => user.CanPacking,
                "Verify" => user.CanVerify,
                "ViewReport" => user.CanViewReport,
                "ManageUser" => user.CanManageUser,
                _ => true
            };

            if (!hasPermission)
            {
                context.Result = new RedirectToActionResult("AccessDenied", "Account", null);
                return;
            }
        }
    }

    private static void RedirectToLogin(AuthorizationFilterContext context)
    {
        var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
        context.Result = new RedirectToActionResult("Login", "Account",
            new { returnUrl });
    }
}
