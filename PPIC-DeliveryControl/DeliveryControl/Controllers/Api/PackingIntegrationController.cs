using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Hubs;
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace DeliveryControl.Controllers.Api
{
    [Route("api/packing")]
    [ApiController]
    [Microsoft.AspNetCore.Authorization.AllowAnonymous]
    public class PackingIntegrationController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<StockHub> _stockHubContext;
        private readonly DeliveryControl.Services.StockCacheService _stockCache;

        public PackingIntegrationController(ApplicationDbContext context, IHubContext<StockHub> stockHubContext, DeliveryControl.Services.StockCacheService stockCache)
        {
            _context = context;
            _stockHubContext = stockHubContext;
            _stockCache = stockCache;
        }

        public class ScanShoppingRequest
        {
            public string Tag { get; set; } = string.Empty;
            public string Label { get; set; } = string.Empty;
            public string Kanban { get; set; } = string.Empty;
            public string User { get; set; } = "PackingSystem";
        }

        [HttpPost("scan-shopping")]
        public async Task<IActionResult> ScanShopping([FromBody] ScanShoppingRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Tag) || string.IsNullOrWhiteSpace(request.Label))
            {
                return BadRequest(new { success = false, message = "Tag and Label are required." });
            }

            var tag = request.Tag.Trim().ToUpper();
            var item = await _context.Items.FirstOrDefaultAsync(i => i.VIN != null && i.VIN.ToUpper() == tag);

            var record = new ShoppingRecord
            {
                Tag = request.Tag.Trim(),
                Label = request.Label.Trim(),
                Kanban = request.Kanban?.Trim() ?? "",
                CreatedBy = request.User ?? "PackingSystem",
                CreatedDate = DateTime.Now,
                ItemId = item?.ItemId,
                Plant = item?.Plant
            };

            _context.ShoppingRecords.Add(record);
            await _context.SaveChangesAsync();
            
            // Hapus cache agar dashboard me-recalculate ulang stok
            _stockCache.Invalidate();

            // Trigger SignalR dashboard update for stock
            await _stockHubContext.Clients.All.SendAsync("updateStock");

            return Ok(new { success = true, message = "Shopping scan recorded successfully." });
        }
    }
}
