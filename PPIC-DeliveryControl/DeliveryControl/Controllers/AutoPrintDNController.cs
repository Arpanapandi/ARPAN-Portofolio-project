using Microsoft.AspNetCore.Mvc;
using DeliveryControl.Models.ViewModels;
using DeliveryControl.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DeliveryControl.Controllers
{
    public class AutoPrintDNController : Controller
    {
        private readonly ISapIntegrationService _sapService;

        public AutoPrintDNController(ISapIntegrationService sapService)
        {
            _sapService = sapService;
        }

        [HttpGet]
        public IActionResult Index()
        {
            // Preset with some dummy values or let the user fill them
            var model = new AutoPrintDNViewModel();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessSO(AutoPrintDNViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = "Validasi gagal: " + string.Join(", ", errors) });
            }

            try
            {
                var soDetails = await _sapService.FetchSoDetailsAsync(model.SONumber, model.SapUrl, model.HeaderKey, model.HeaderValue, model.ShippingPoint);
                return Json(new { 
                    success = true, 
                    message = $"Berhasil menarik data SAP!",
                    data = soDetails
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Gagal memproses data SO dari SAP: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateOD(CreateOrderDeliveryViewModel model)
        {
            if (!ModelState.IsValid)
            {
                var errors = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage);
                return Json(new { success = false, message = "Validasi gagal: " + string.Join(", ", errors) });
            }

            try
            {
                var newSchedule = await _sapService.SaveCreateODAsync(model);
                var ci = new System.Globalization.CultureInfo("de-DE");
                return Json(new { 
                    success = true, 
                    message = $"Berhasil membuat Outbound Delivery!",
                    data = new {
                        scheduleNumber = newSchedule.ScheduleNumber,
                        scheduledDate = newSchedule.ScheduledDate.ToString("dd.MM.yyyy"),
                        documentDate = DateTime.Now.ToString("dd.MM.yyyy"),
                        shipToParty = string.IsNullOrWhiteSpace(model.ShipToParty) ? "AHM15001" : model.ShipToParty,
                        shipToPartyName = "PT. ASTRA HONDA MOTOR / KAWASAN INDUSTRI INDOTAISEI / 00000 KARAWANG JAWA BARAT",
                        soldToParty = model.SoldToParty,
                        status = newSchedule.Status,
                        notes = newSchedule.Notes,
                        items = model.Items != null ? model.Items.Select((item, index) => {
                            string qtyStr = item.OdQty % 1 == 0 && item.OdQty < 1000 ? item.OdQty.ToString("0") : item.OdQty.ToString("#,##0.000", ci);
                            return (object)new {
                                itm = (index + 1) * 10,
                                material = item.ItemCode,
                                delivQty = qtyStr,
                                unit = "PCS",
                                description = item.Description,
                                reqSegment = "",
                                stockSegment = "",
                                batchSplit = "",
                                itemCategory = (index == 2 || index == 4) ? "TAX" : "ZMAN C",
                                plant = "C",
                                volume = "",
                                batch = ""
                            };
                        }).ToList() : new List<object>()
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Gagal menyimpan OD: {ex.Message}" });
            }
        }
    }
}
