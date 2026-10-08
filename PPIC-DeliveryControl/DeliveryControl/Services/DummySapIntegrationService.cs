using DeliveryControl.Data;
using DeliveryControl.Models;
using DeliveryControl.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DeliveryControl.Services
{
    public class DummySapIntegrationService : ISapIntegrationService
    {
        private readonly ApplicationDbContext _context;

        public DummySapIntegrationService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<DeliverySchedule> FetchSoAndConvertToOdAsync(string soNumber, string url, string headerKey, string headerValue, string shippingPoint)
        {
            // Simulate network delay for calling SAP
            await Task.Delay(1500);

            var customer = _context.Customers.FirstOrDefault();
            if (customer == null)
            {
                customer = new Customer { CustomerCode = "DUMMY-" + DateTime.Now.ToString("HHmmss"), CustomerName = "Dummy Customer", CreatedDate = DateTime.Now };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }

            var newOd = new DeliverySchedule
            {
                ScheduleNumber = "OD-" + soNumber + "-" + DateTime.Now.ToString("HHmmss"),
                CustomerId = customer.CustomerId,
                Route = "DUMMY-ROUTE",
                Cycle = "1",
                EnterDockTime = DateTime.Today.AddHours(8),
                PickupTime = DateTime.Today.AddHours(9),
                ETD = DateTime.Today.AddHours(10),
                ScheduledDate = DateTime.Today,
                Status = "Scheduled",
                DriverStatus = "Scheduled",
                PreparationStatus = "Scheduled",
                Notes = $"Generated from SAP SO: {soNumber}. URL: {url}. Shipping Point: {shippingPoint}",
                CreatedDate = DateTime.Now,
                CreatedBy = "System"
            };

            var dummyItem = _context.Items.FirstOrDefault();
            if (dummyItem == null)
            {
                dummyItem = new Item { ItemCode = "DUMMY-ITM-" + DateTime.Now.ToString("HHmmss"), ItemName = "Dummy Item", CreatedDate = DateTime.Now };
                _context.Items.Add(dummyItem);
                await _context.SaveChangesAsync();
            }
            
            newOd.DeliveryItems.Add(new DeliveryItem
            {
                ItemId = dummyItem.ItemId,
                Quantity = 100,
                Notes = "Item from SAP"
            });

            _context.DeliverySchedules.Add(newOd);
            await _context.SaveChangesAsync();

            return newOd;
        }

        public async Task<SapSalesOrderDto> FetchSoDetailsAsync(string soNumber, string url, string headerKey, string headerValue, string shippingPoint)
        {
            await Task.Delay(800); // Simulate network delay

            var items = new List<SapSalesOrderItemDto>
            {
                new SapSalesOrderItemDto
                {
                    ItemCode = "VMVRIP-FM1750M-FG",
                    PartNumber = "VMVRIP-FM1750M-FG",
                    Description = "RB BUSH ENGINE HANGER H2-11103-KVB-U100",
                    CustMaterialInfo = "AHM-MAT-01",
                    ShipToMaterial = "AHM-MAT-01",
                    Barcode = "VMVRIP-FM1750M-FG",
                    OdQty = 2500,
                    Sloc = "SLOC-A"
                },
                new SapSalesOrderItemDto
                {
                    ItemCode = "VMFUNC-FM1900M-FG",
                    PartNumber = "VMFUNC-FM1900M-FG",
                    Description = "GASKET COMP HEAD COVER 12391-K1AL-N810-M",
                    CustMaterialInfo = "AHM-MAT-02",
                    ShipToMaterial = "AHM-MAT-02",
                    Barcode = "VMFUNC-FM1900M-FG",
                    OdQty = 21,
                    Sloc = "SLOC-A"
                },
                new SapSalesOrderItemDto
                {
                    ItemCode = "VMFUNC-FM190AM-SR",
                    PartNumber = "VMFUNC-FM190AM-SR",
                    Description = "SERVICE PART GASKET 12391-K1AL-N810-M",
                    CustMaterialInfo = "AHM-MAT-03",
                    ShipToMaterial = "AHM-MAT-03",
                    Barcode = "VMFUNC-FM190AM-SR",
                    OdQty = 21,
                    Sloc = "SLOC-B"
                },
                new SapSalesOrderItemDto
                {
                    ItemCode = "VMFUNC-FM1920M-FG",
                    PartNumber = "VMFUNC-FM1920M-FG",
                    Description = "GASKET HEAD COVER 12391-KVY-9000",
                    CustMaterialInfo = "AHM-MAT-04",
                    ShipToMaterial = "AHM-MAT-04",
                    Barcode = "VMFUNC-FM1920M-FG",
                    OdQty = 1000,
                    Sloc = "SLOC-A"
                },
                new SapSalesOrderItemDto
                {
                    ItemCode = "VMFUNC-FM1920M-SR",
                    PartNumber = "VMFUNC-FM1920M-SR",
                    Description = "SERVICE PART GASKET H C 12391-KVY-9000",
                    CustMaterialInfo = "AHM-MAT-05",
                    ShipToMaterial = "AHM-MAT-05",
                    Barcode = "VMFUNC-FM1920M-SR",
                    OdQty = 1000,
                    Sloc = "SLOC-B"
                },
                new SapSalesOrderItemDto
                {
                    ItemCode = "VMFUNC-FM1910M-FG",
                    PartNumber = "VMFUNC-FM1910M-FG",
                    Description = "GASKET HEAD COVER 12391-KZR-6000",
                    CustMaterialInfo = "AHM-MAT-06",
                    ShipToMaterial = "AHM-MAT-06",
                    Barcode = "VMFUNC-FM1910M-FG",
                    OdQty = 8000,
                    Sloc = "SLOC-A"
                }
            };

            var dto = new SapSalesOrderDto
            {
                SalesOrderNumber = string.IsNullOrWhiteSpace(soNumber) ? "3660772684" : soNumber,
                PurchaseOrderNumber = "PO-AHM-" + DateTime.Now.ToString("yyMMdd"),
                ShippingPoint = string.IsNullOrWhiteSpace(shippingPoint) ? "2501" : shippingPoint,
                Items = items
            };

            return dto;
        }

        public async Task<DeliverySchedule> SaveCreateODAsync(DeliveryControl.Models.ViewModels.CreateOrderDeliveryViewModel model)
        {
            var customer = _context.Customers.FirstOrDefault(c => c.CustomerCode == "AHM15001");
            if (customer == null)
            {
                customer = new Customer { 
                    CustomerCode = "AHM15001", 
                    CustomerName = "PT. ASTRA HONDA MOTOR / KAWASAN INDUSTRI INDOTAISEI / 00000 KARAWANG JAWA BARAT", 
                    CreatedDate = DateTime.Now 
                };
                _context.Customers.Add(customer);
                await _context.SaveChangesAsync();
            }

            // Generate SAP-like 10-digit number e.g. 366077xxxx or preserve format
            string scheduleNum = "3660" + (new Random().Next(100000, 999999)).ToString();

            var newOd = new DeliverySchedule
            {
                ScheduleNumber = scheduleNum,
                CustomerId = customer.CustomerId, 
                Route = model.RouteNumber ?? "RT-01",
                Cycle = "1",
                EnterDockTime = model.DeliveryDate.Date.AddHours(8),
                PickupTime = model.DeliveryDate.Date.AddHours(9),
                ETD = model.DeliveryDate.Date.AddHours(10),
                ScheduledDate = model.DeliveryDate,
                Status = "Scheduled",
                DriverStatus = "Scheduled",
                PreparationStatus = "Scheduled",
                VehicleNumber = model.VehicleNumber,
                Notes = $"PO: {model.PurchaseOrder}. Sold-to: {model.SoldToParty}. Ship-to: {model.ShipToParty}. Dock: {model.DockCode}. Delivery Text: {model.DeliveryText}",
                CreatedDate = DateTime.Now,
                CreatedBy = "System"
            };

            if (model.Items != null)
            {
                foreach (var item in model.Items)
                {
                    var dbItem = _context.Items.FirstOrDefault(i => i.ItemCode == item.ItemCode);
                    if (dbItem == null)
                    {
                        dbItem = new Item { ItemCode = item.ItemCode ?? "DUMMY-ITM", ItemName = item.Description ?? "Dummy Item", CreatedDate = DateTime.Now };
                        _context.Items.Add(dbItem);
                        await _context.SaveChangesAsync();
                    }

                    newOd.DeliveryItems.Add(new DeliveryItem
                    {
                        ItemId = dbItem.ItemId,
                        Quantity = item.OdQty,
                        Notes = $"Barcode: {item.Barcode}, SLOC: {item.Sloc}, PN: {item.PartNumber}"
                    });
                }
            }

            _context.DeliverySchedules.Add(newOd);
            await _context.SaveChangesAsync();

            return newOd;
        }
    }
}
