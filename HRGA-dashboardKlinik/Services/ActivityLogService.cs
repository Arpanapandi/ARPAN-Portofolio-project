using dashboardKlinik.Data;
using dashboardKlinik.Models;

namespace dashboardKlinik.Services
{
    public class ActivityLogService
    {
        private readonly ApplicationDbContext _context;

        public ActivityLogService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task LogAsync(string action, string description, string entityType = "", int? entityId = null)
        {
            var log = new ActivityLog
            {
                Action = action,
                Description = description,
                EntityType = entityType,
                EntityId = entityId,
                Timestamp = DateTime.Now
            };

            _context.ActivityLog.Add(log);
            await _context.SaveChangesAsync();
        }
    }
}
