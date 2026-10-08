using DeliveryControl.Data;
using DeliveryControl.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace DeliveryControl.Services
{
    /// <summary>
    /// Service untuk mencatat aktivitas di sistem
    /// </summary>
    public class ActivityLogService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public ActivityLogService(IServiceScopeFactory scopeFactory, IHttpContextAccessor httpContextAccessor)
        {
            _scopeFactory = scopeFactory;
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Log aktivitas ke database (Non-blocking / Background Fire-and-Forget)
        /// </summary>
        public Task LogActivity(
            string module,
            string action,
            string? entityName = null,
            int? entityId = null,
            string? description = null,
            string? oldData = null,
            string? newData = null,
            string? performedBy = null,
            bool autoSave = true)
        {
            try
            {
                var httpContext = _httpContextAccessor.HttpContext;
                var ipAddress = httpContext?.Connection?.RemoteIpAddress?.ToString();
                var userAgent = httpContext?.Request?.Headers["User-Agent"].ToString();
                var user = performedBy ?? httpContext?.User?.Identity?.Name ?? "System";

                var log = new ActivityLog
                {
                    Module = module,
                    Action = action,
                    EntityName = entityName,
                    EntityId = entityId,
                    Description = description,
                    OldData = oldData,
                    NewData = newData,
                    Timestamp = DateTime.Now,
                    PerformedBy = user,
                    IpAddress = ipAddress,
                    UserAgent = userAgent
                };

                // Fire and forget, don't wait for save
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        dbContext.ActivityLogs.Add(log);
                        if (autoSave)
                        {
                            await dbContext.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error logging activity (bg): {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                // Log error tapi jangan sampai mengganggu proses utama
                Console.WriteLine($"Error logging activity: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Log Create activity
        /// </summary>
        public async Task LogCreate(string module, string entityName, int entityId, string description, string? performedBy = null, bool autoSave = true)
        {
            await LogActivity(module, "Create", entityName, entityId, description, performedBy: performedBy, autoSave: autoSave);
        }

        /// <summary>
        /// Log Update activity
        /// </summary>
        public async Task LogUpdate(string module, string entityName, int entityId, string description, string? oldData = null, string? newData = null, string? performedBy = null, bool autoSave = true)
        {
            await LogActivity(module, "Update", entityName, entityId, description, oldData, newData, performedBy, autoSave);
        }

        /// <summary>
        /// Log Delete activity
        /// </summary>
        public async Task LogDelete(string module, string entityName, int entityId, string description, string? performedBy = null, bool autoSave = true)
        {
            await LogActivity(module, "Delete", entityName, entityId, description, performedBy: performedBy, autoSave: autoSave);
        }

        /// <summary>
        /// Log Confirm activity (untuk Driver/Preparation)
        /// </summary>
        public async Task LogConfirm(string module, string entityName, int entityId, string description, string? performedBy = null, bool autoSave = true)
        {
            await LogActivity(module, "Confirm", entityName, entityId, description, performedBy: performedBy, autoSave: autoSave);
        }
    }
}

