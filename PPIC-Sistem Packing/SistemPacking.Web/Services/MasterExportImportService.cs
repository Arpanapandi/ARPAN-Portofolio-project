using SistemPacking.Web.Helpers;
using ClosedXML.Excel;
using SistemPacking.Web.Models;
using SistemPacking.Web.Interfaces;

namespace SistemPacking.Web.Services;

public interface IMasterExportImportService
{
    // Items
    byte[] GenerateItemTemplate();
    Task<byte[]> ExportItemsAsync();
    Task<UploadResultDto> ImportItemsAsync(Stream fileStream, Dictionary<string, string>? spisMap = null, Dictionary<string, string>? sppsMap = null);
    
    // ItemMappings
    byte[] GenerateItemMappingTemplate();
    Task<byte[]> ExportItemMappingsAsync();
    Task<UploadResultDto> ImportItemMappingsAsync(Stream fileStream);
    
    // Customers
    byte[] GenerateCustomerTemplate();
    Task<byte[]> ExportCustomersAsync();
    Task<UploadResultDto> ImportCustomersAsync(Stream fileStream);
    
    // Areas
    byte[] GenerateAreaTemplate();
    Task<byte[]> ExportAreasAsync();
    Task<UploadResultDto> ImportAreasAsync(Stream fileStream);

    // FGLocations
    byte[] GenerateFGLocationTemplate();
    Task<byte[]> ExportFGLocationsAsync();
    Task<UploadResultDto> ImportFGLocationsAsync(Stream fileStream);

    // Shifts
    byte[] GenerateShiftTemplate();
    Task<byte[]> ExportShiftsAsync();
    Task<UploadResultDto> ImportShiftsAsync(Stream fileStream);

    // ManPower
    byte[] GenerateManPowerTemplate();
    Task<byte[]> ExportManPowerAsync();
    Task<UploadResultDto> ImportManPowerAsync(Stream fileStream);

    // ReasonRejects
    byte[] GenerateReasonRejectTemplate();
    Task<byte[]> ExportReasonRejectsAsync();
    Task<UploadResultDto> ImportReasonRejectsAsync(Stream fileStream);

    // Users
    byte[] GenerateUserTemplate();
    Task<byte[]> ExportUsersAsync();
    Task<UploadResultDto> ImportUsersAsync(Stream fileStream);
}

public class MasterExportImportService : IMasterExportImportService
{
    private readonly IUnitOfWork _uow;

    public MasterExportImportService(IUnitOfWork uow)
    {
        _uow = uow;
    }

    // --- HELPER METHODS ---
    private byte[] GetBytes(XLWorkbook wb)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }
    
    // ==============================================
    // ITEMS
    // ==============================================
    public byte[] GenerateItemTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Items");
        
        ws.Cell(1, 1).Value = "Cara Isi: Mulai pengisian data pada baris ke-3. Kolom 'No' hanya untuk penomoran.";
        ws.Range("A1:L1").Merge();
        ws.Cell(1, 1).Style.Font.Italic = true;

        var headers = new[] { "No", "VIN", "Part No", "Part Name", "Dock", "Type Karton", "Type Plastik", "Point 1", "Point 2", "Point 3", "Point 4", "Point 5" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(2, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#B4C6E7");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }
        ws.Columns().AdjustToContents();
        return GetBytes(wb);
    }

    public async Task<byte[]> ExportItemsAsync()
    {
        var items = await _uow.Items.GetWithDetailsAsync();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Items");
        var headers = new[] { "No", "VIN", "Part No", "Part Name", "Dock", "Type Karton", "Type Plastik", "Point 1", "Point 2", "Point 3", "Point 4", "Point 5", "ProdPlant", "LokasiRack", "Rack", "NoRack", "QtyPcs", "ActQty", "Min", "Rop", "Max" };
        for (int i = 0; i < headers.Length; i++)
        {
            var cell = ws.Cell(1, i + 1);
            cell.Value = headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#B4C6E7");
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        }

        int row = 2;
        int no = 1;
        foreach (var item in items)
        {
            ws.Cell(row, 1).Value = no++;
            ws.Cell(row, 2).Value = item.VIN;
            ws.Cell(row, 3).Value = item.ItemCode;
            ws.Cell(row, 4).Value = item.ItemName;
            ws.Cell(row, 5).Value = item.Dock;
            ws.Cell(row, 6).Value = item.TypeKarton;
            ws.Cell(row, 7).Value = item.TypePlastik;
            ws.Cell(row, 8).Value = item.Point1;
            ws.Cell(row, 9).Value = item.Point2;
            ws.Cell(row, 10).Value = item.Point3;
            ws.Cell(row, 11).Value = item.Point4;
            ws.Cell(row, 12).Value = item.Point5;
            ws.Cell(row, 13).Value = item.ProdPlant;
            ws.Cell(row, 14).Value = item.LokasiRack;
            ws.Cell(row, 15).Value = item.Rack;
            ws.Cell(row, 16).Value = item.NoRack;
            ws.Cell(row, 17).Value = item.QtyPcs;
            ws.Cell(row, 18).Value = item.ActQty;
            ws.Cell(row, 19).Value = item.Min;
            ws.Cell(row, 20).Value = item.Rop;
            ws.Cell(row, 21).Value = item.Max;
            row++;
        }
        ws.Columns().AdjustToContents();
        return GetBytes(wb);
    }

    public async Task<UploadResultDto> ImportItemsAsync(Stream fileStream, Dictionary<string, string>? spisMap = null, Dictionary<string, string>? sppsMap = null)
    {
        var result = new UploadResultDto();
        var errors = new List<string>();
        int successRows = 0;

        try
        {
            using var wb = new XLWorkbook(fileStream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            
            var customers = (await _uow.Customers.GetAllAsync()).ToList();
            var allData = (await _uow.Items.GetAllIncludeDeletedAsync()).ToList();

            int startRow = 2;
            if (ws.Cell(1, 1).GetString().StartsWith("Cara Isi", StringComparison.OrdinalIgnoreCase))
            {
                startRow = 3;
            }

            var processedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int customerId = customers.FirstOrDefault()?.Id ?? 1;

            for (int row = startRow; row <= lastRow; row++)
            {
                try
                {
                    var vin = ws.Cell(row, 2).GetString().Trim();
                    var itemCode = ws.Cell(row, 3).GetString().Trim();
                    var itemName = ws.Cell(row, 4).GetString().Trim();
                    var dock = ws.Cell(row, 5).GetString().Trim();
                    var typeKarton = ws.Cell(row, 6).GetString().Trim();
                    var typePlastik = ws.Cell(row, 7).GetString().Trim();
                    var point1 = ws.Cell(row, 8).GetString().Trim();
                    var point2 = ws.Cell(row, 9).GetString().Trim();
                    var point3 = ws.Cell(row, 10).GetString().Trim();
                    var point4 = ws.Cell(row, 11).GetString().Trim();
                    var point5 = ws.Cell(row, 12).GetString().Trim();
                    var prodPlant = ws.Cell(row, 13).GetString().Trim();
                    var lokasiRack = ws.Cell(row, 14).GetString().Trim();
                    var rack = ws.Cell(row, 15).GetString().Trim();
                    var noRack = ws.Cell(row, 16).GetString().Trim();
                    decimal.TryParse(ws.Cell(row, 17).GetString().Trim(), out decimal qtyPcs);
                    decimal.TryParse(ws.Cell(row, 18).GetString().Trim(), out decimal actQty);
                    decimal.TryParse(ws.Cell(row, 19).GetString().Trim(), out decimal min);
                    decimal.TryParse(ws.Cell(row, 20).GetString().Trim(), out decimal rop);
                    decimal.TryParse(ws.Cell(row, 21).GetString().Trim(), out decimal max);
                    
                    var barcode = itemCode; // Fallback

                    if (string.IsNullOrEmpty(vin) && string.IsNullOrEmpty(itemCode) && string.IsNullOrEmpty(itemName)) continue; // skip empty rows

                    if (string.IsNullOrEmpty(vin) || string.IsNullOrEmpty(dock) || string.IsNullOrEmpty(itemCode))
                    {
                        errors.Add($"Row {row}: VIN, Part No, dan Dock wajib diisi sebagai identitas.");
                        continue;
                    }

                    var key = $"{vin}_{dock}_{itemCode}".ToUpper();
                    if (!processedCodes.Add(key))
                    {
                        errors.Add($"Row {row}: Kombinasi VIN+Dock+PartNo '{key}' terduplikasi di dalam file Excel.");
                        continue;
                    }

                    var existing = allData.FirstOrDefault(x => string.Equals(x.VIN, vin, StringComparison.OrdinalIgnoreCase) 
                                                            && string.Equals(x.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase)
                                                            && string.Equals(x.Dock, dock, StringComparison.OrdinalIgnoreCase));
                    
                    var codeConflict = allData.FirstOrDefault(x => string.Equals(x.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase) && x != existing);
                    if (codeConflict != null)
                    {
                        errors.Add($"Row {row}: Part No '{itemCode}' sudah digunakan oleh kombinasi VIN+Dock lain.");
                        continue;
                    }

                    if (existing == null)
                    {
                        // Insert
                        var newItem = new Item
                        {
                            Dock = dock,
                            VIN = vin,
                            ItemCode = itemCode,
                            ItemName = itemName,
                            TypeKarton = typeKarton,
                            TypePlastik = typePlastik,
                            Point1 = point1,
                            Point2 = point2,
                            Point3 = point3,
                            Point4 = point4,
                            Point5 = point5,
                            ProdPlant = prodPlant,
                            LokasiRack = lokasiRack,
                            Rack = rack,
                            NoRack = noRack,
                            QtyPcs = qtyPcs,
                            ActQty = actQty,
                            Min = min,
                            Rop = rop,
                            Max = max,
                            Barcode = barcode,
                            CustomerId = customerId,
                            UOM = "PCS",
                            PackingStandard = 1,
                            Status = ItemStatus.Active
                        };
                        
                        if (spisMap != null)
                        {
                            if (spisMap.TryGetValue(itemCode.ToUpper(), out var spis)) newItem.SpisImagePath = spis;
                            else if (spisMap.TryGetValue(vin.ToUpper(), out var spis2)) newItem.SpisImagePath = spis2;
                        }
                        
                        if (sppsMap != null)
                        {
                            if (sppsMap.TryGetValue(itemCode.ToUpper(), out var spps)) newItem.SppsImagePath = spps;
                            else if (sppsMap.TryGetValue(vin.ToUpper(), out var spps2)) newItem.SppsImagePath = spps2;
                        }

                        await _uow.Items.AddAsync(newItem);
                        allData.Add(newItem);
                    }
                    else
                    {
                        // Update (or restore if deleted)
                        existing.IsDeleted = false;
                        existing.ItemName = itemName;
                        existing.TypeKarton = typeKarton;
                        existing.TypePlastik = typePlastik;
                        existing.Point1 = point1;
                        existing.Point2 = point2;
                        existing.Point3 = point3;
                        existing.Point4 = point4;
                        existing.Point5 = point5;
                        existing.ProdPlant = prodPlant;
                        existing.LokasiRack = lokasiRack;
                        existing.Rack = rack;
                        existing.NoRack = noRack;
                        existing.QtyPcs = qtyPcs;
                        existing.ActQty = actQty;
                        existing.Min = min;
                        existing.Rop = rop;
                        existing.Max = max;

                        if (spisMap != null)
                        {
                            if (spisMap.TryGetValue(itemCode.ToUpper(), out var spis)) existing.SpisImagePath = spis;
                            else if (spisMap.TryGetValue(vin.ToUpper(), out var spis2)) existing.SpisImagePath = spis2;
                        }
                        
                        if (sppsMap != null)
                        {
                            if (sppsMap.TryGetValue(itemCode.ToUpper(), out var spps)) existing.SppsImagePath = spps;
                            else if (sppsMap.TryGetValue(vin.ToUpper(), out var spps2)) existing.SppsImagePath = spps2;
                        }

                        await _uow.Items.UpdateAsync(existing);
                    }
                    
                    successRows++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Row {row}: Error format - {ex.Message}");
                }
            }
            await _uow.SaveChangesAsync();

            result.Success = successRows > 0 || errors.Count == 0;
            result.Message = $"Import selesai. {successRows} sukses, {errors.Count} gagal.";
            result.SuccessRows = successRows;
            result.FailedRows = errors.Count;
            result.Errors = errors;
        }
        catch (Exception ex)
        {
            result.Success = false;
            var innerMsg = ex.InnerException != null ? ex.InnerException.Message : "";
            result.Message = $"Gagal membaca file: {ex.Message} {innerMsg}";
        }
        return result;
    }
    
    // ==============================================
    // CUSTOMERS
    // ==============================================
    public byte[] GenerateCustomerTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Customers");
        ws.Cell(1, 1).Value = "CustomerCode";
        ws.Cell(1, 2).Value = "CustomerName";
        ws.Cell(1, 3).Value = "Address";
        ws.Cell(1, 4).Value = "ContactPerson";
        ws.Cell(1, 5).Value = "Phone";
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        return GetBytes(wb);
    }

    public async Task<byte[]> ExportCustomersAsync()
    {
        var data = await _uow.Customers.GetAllAsync();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Customers");
        ws.Cell(1, 1).Value = "CustomerCode";
        ws.Cell(1, 2).Value = "CustomerName";
        ws.Cell(1, 3).Value = "Address";
        ws.Cell(1, 4).Value = "ContactPerson";
        ws.Cell(1, 5).Value = "Phone";
        ws.Cell(1, 6).Value = "IsActive";
        ws.Row(1).Style.Font.Bold = true;

        int row = 2;
        foreach (var item in data)
        {
            ws.Cell(row, 1).Value = item.CustomerCode;
            ws.Cell(row, 2).Value = item.CustomerName;
            ws.Cell(row, 3).Value = item.Address;
            ws.Cell(row, 4).Value = item.ContactPerson;
            ws.Cell(row, 5).Value = item.Phone;
            ws.Cell(row, 6).Value = item.IsActive ? "Yes" : "No";
            row++;
        }
        ws.Columns().AdjustToContents();
        return GetBytes(wb);
    }

    public async Task<UploadResultDto> ImportCustomersAsync(Stream fileStream)
    {
        var result = new UploadResultDto();
        var errors = new List<string>();
        int successRows = 0;

        try
        {
            using var wb = new XLWorkbook(fileStream);
            var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var allData = (await _uow.Customers.GetAllAsync()).ToList();

            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var code = ws.Cell(row, 1).GetString().Trim();
                    var name = ws.Cell(row, 2).GetString().Trim();
                    var address = ws.Cell(row, 3).GetString().Trim();
                    var cp = ws.Cell(row, 4).GetString().Trim();
                    var phone = ws.Cell(row, 5).GetString().Trim();

                    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name))
                    {
                        errors.Add($"Row {row}: CustomerCode dan CustomerName wajib diisi.");
                        continue;
                    }

                    var existing = allData.FirstOrDefault(x => x.CustomerCode.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (existing == null)
                    {
                        var newItem = new Customer
                        {
                            CustomerCode = code, CustomerName = name,
                            Address = address, ContactPerson = cp, Phone = phone, IsActive = true
                        };
                        await _uow.Customers.AddAsync(newItem);
                        allData.Add(newItem); // cache
                    }
                    else
                    {
                        existing.CustomerName = name; existing.Address = address;
                        existing.ContactPerson = cp; existing.Phone = phone;
                        await _uow.Customers.UpdateAsync(existing);
                    }
                    successRows++;
                }
                catch (Exception ex)
                {
                    errors.Add($"Row {row}: {ex.Message}");
                }
            }
            await _uow.SaveChangesAsync();
            result.Success = successRows > 0 || errors.Count == 0;
            result.Message = $"Import selesai. {successRows} sukses, {errors.Count} gagal.";
            result.SuccessRows = successRows; result.FailedRows = errors.Count; result.Errors = errors;
        }
        catch (Exception ex) { result.Success = false; result.Message = "Error: " + ex.Message; }
        return result;
    }

    // ==============================================
    // AREAS
    // ==============================================
    public byte[] GenerateAreaTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Areas");
        ws.Cell(1, 1).Value = "AreaCode";
        ws.Cell(1, 2).Value = "AreaName";
        ws.Cell(1, 3).Value = "Description";
        ws.Row(1).Style.Font.Bold = true;
        ws.Columns().AdjustToContents();
        return GetBytes(wb);
    }
    public async Task<byte[]> ExportAreasAsync()
    {
        var data = await _uow.Areas.GetAllAsync();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Areas");
        ws.Cell(1, 1).Value = "AreaCode"; ws.Cell(1, 2).Value = "AreaName"; ws.Cell(1, 3).Value = "Description";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var item in data)
        {
            ws.Cell(row, 1).Value = item.AreaCode; ws.Cell(row, 2).Value = item.AreaName; ws.Cell(row, 3).Value = item.Description;
            row++;
        }
        ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<UploadResultDto> ImportAreasAsync(Stream fileStream)
    {
        var result = new UploadResultDto(); var errors = new List<string>(); int successRows = 0;
        try
        {
            using var wb = new XLWorkbook(fileStream); var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var allData = (await _uow.Areas.GetAllAsync()).ToList();
            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var code = ws.Cell(row, 1).GetString().Trim(); var name = ws.Cell(row, 2).GetString().Trim(); var desc = ws.Cell(row, 3).GetString().Trim();
                    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name)) { errors.Add($"Row {row}: AreaCode dan AreaName wajib."); continue; }
                    var existing = allData.FirstOrDefault(x => x.AreaCode.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) {
                        var newItem = new Area { AreaCode = code, AreaName = name, Description = desc };
                        await _uow.Areas.AddAsync(newItem); allData.Add(newItem);
                    } else {
                        existing.AreaName = name; existing.Description = desc; await _uow.Areas.UpdateAsync(existing);
                    }
                    successRows++;
                }
                catch (Exception ex) { errors.Add($"Row {row}: {ex.Message}"); }
            }
            await _uow.SaveChangesAsync();
            result.Success = successRows > 0 || errors.Count == 0; result.SuccessRows = successRows; result.FailedRows = errors.Count; result.Errors = errors; result.Message = "Import selesai";
        }
        catch (Exception ex) { result.Success = false; result.Message = "Error: " + ex.Message; }
        return result;
    }

    // ==============================================
    // FGLOCATIONS
    // ==============================================
    public byte[] GenerateFGLocationTemplate()
    {
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("FGLocations");
        ws.Cell(1, 1).Value = "LocationCode"; ws.Cell(1, 2).Value = "LocationName"; ws.Cell(1, 3).Value = "AreaCode";
        ws.Row(1).Style.Font.Bold = true; ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<byte[]> ExportFGLocationsAsync()
    {
        var data = await _uow.FGLocations.GetAllAsync();
        var areas = await _uow.Areas.GetAllAsync(); // needed to map AreaId to AreaCode
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("FGLocations");
        ws.Cell(1, 1).Value = "LocationCode"; ws.Cell(1, 2).Value = "LocationName"; ws.Cell(1, 3).Value = "AreaCode"; ws.Cell(1, 4).Value = "IsActive";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var item in data)
        {
            var area = areas.FirstOrDefault(a => a.Id == item.AreaId);
            ws.Cell(row, 1).Value = item.LocationCode; ws.Cell(row, 2).Value = item.LocationName; ws.Cell(row, 3).Value = area?.AreaCode ?? ""; ws.Cell(row, 4).Value = item.IsActive ? "Yes" : "No";
            row++;
        }
        ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<UploadResultDto> ImportFGLocationsAsync(Stream fileStream)
    {
        var result = new UploadResultDto(); var errors = new List<string>(); int successRows = 0;
        try
        {
            using var wb = new XLWorkbook(fileStream); var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var allData = (await _uow.FGLocations.GetAllAsync()).ToList();
            var areas = (await _uow.Areas.GetAllAsync()).ToList();
            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var code = ws.Cell(row, 1).GetString().Trim(); var name = ws.Cell(row, 2).GetString().Trim(); var areaCode = ws.Cell(row, 3).GetString().Trim();
                    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(areaCode)) { errors.Add($"Row {row}: LocationCode, LocationName, AreaCode wajib."); continue; }
                    
                    var area = areas.FirstOrDefault(a => a.AreaCode.Equals(areaCode, StringComparison.OrdinalIgnoreCase));
                    if(area == null) { errors.Add($"Row {row}: AreaCode '{areaCode}' tidak ditemukan."); continue; }

                    var existing = allData.FirstOrDefault(x => x.LocationCode.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) {
                        var newItem = new FGLocation { LocationCode = code, LocationName = name, AreaId = area.Id, IsActive = true };
                        await _uow.FGLocations.AddAsync(newItem); allData.Add(newItem);
                    } else {
                        existing.LocationName = name; existing.AreaId = area.Id; await _uow.FGLocations.UpdateAsync(existing);
                    }
                    successRows++;
                }
                catch (Exception ex) { errors.Add($"Row {row}: {ex.Message}"); }
            }
            await _uow.SaveChangesAsync();
            result.Success = successRows > 0 || errors.Count == 0; result.SuccessRows = successRows; result.FailedRows = errors.Count; result.Errors = errors; result.Message = "Import selesai";
        }
        catch (Exception ex) { result.Success = false; result.Message = "Error: " + ex.Message; }
        return result;
    }

    // ==============================================
    // SHIFTS
    // ==============================================
    public byte[] GenerateShiftTemplate()
    {
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("Shifts");
        ws.Cell(1, 1).Value = "ShiftName"; ws.Cell(1, 2).Value = "StartTime (HH:mm)"; ws.Cell(1, 3).Value = "EndTime (HH:mm)";
        ws.Row(1).Style.Font.Bold = true; ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<byte[]> ExportShiftsAsync()
    {
        var data = await _uow.Shifts.GetAllAsync();
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("Shifts");
        ws.Cell(1, 1).Value = "ShiftName"; ws.Cell(1, 2).Value = "StartTime"; ws.Cell(1, 3).Value = "EndTime"; ws.Cell(1, 4).Value = "IsActive";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var item in data)
        {
            ws.Cell(row, 1).Value = item.ShiftName; ws.Cell(row, 2).Value = item.StartTime.ToString(@"hh\:mm"); ws.Cell(row, 3).Value = item.EndTime.ToString(@"hh\:mm"); ws.Cell(row, 4).Value = item.IsActive ? "Yes" : "No";
            row++;
        }
        ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<UploadResultDto> ImportShiftsAsync(Stream fileStream)
    {
        var result = new UploadResultDto(); var errors = new List<string>(); int successRows = 0;
        try
        {
            using var wb = new XLWorkbook(fileStream); var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var allData = (await _uow.Shifts.GetAllAsync()).ToList();
            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var name = ws.Cell(row, 1).GetString().Trim(); var startStr = ws.Cell(row, 2).GetString().Trim(); var endStr = ws.Cell(row, 3).GetString().Trim();
                    if (string.IsNullOrEmpty(name)) { errors.Add($"Row {row}: ShiftName wajib."); continue; }
                    
                    TimeSpan.TryParse(startStr, out TimeSpan start); TimeSpan.TryParse(endStr, out TimeSpan end);

                    var existing = allData.FirstOrDefault(x => x.ShiftName.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) {
                        var newItem = new Shift { ShiftName = name, StartTime = start, EndTime = end, IsActive = true };
                        await _uow.Shifts.AddAsync(newItem); allData.Add(newItem);
                    } else {
                        existing.StartTime = start; existing.EndTime = end; await _uow.Shifts.UpdateAsync(existing);
                    }
                    successRows++;
                }
                catch (Exception ex) { errors.Add($"Row {row}: {ex.Message}"); }
            }
            await _uow.SaveChangesAsync();
            result.Success = successRows > 0 || errors.Count == 0; result.SuccessRows = successRows; result.FailedRows = errors.Count; result.Errors = errors; result.Message = "Import selesai";
        }
        catch (Exception ex) { result.Success = false; result.Message = "Error: " + ex.Message; }
        return result;
    }

    // ==============================================
    // REASON REJECTS
    // ==============================================
    public byte[] GenerateReasonRejectTemplate()
    {
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("ReasonRejects");
        ws.Cell(1, 1).Value = "ReasonCode"; ws.Cell(1, 2).Value = "Description";
        ws.Row(1).Style.Font.Bold = true; ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<byte[]> ExportReasonRejectsAsync()
    {
        var data = await _uow.ReasonRejects.GetAllAsync();
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("ReasonRejects");
        ws.Cell(1, 1).Value = "ReasonCode"; ws.Cell(1, 2).Value = "Description"; ws.Cell(1, 3).Value = "IsActive";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var item in data)
        {
            ws.Cell(row, 1).Value = item.ReasonCode; ws.Cell(row, 2).Value = item.Description; ws.Cell(row, 3).Value = item.IsActive ? "Yes" : "No";
            row++;
        }
        ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<UploadResultDto> ImportReasonRejectsAsync(Stream fileStream)
    {
        var result = new UploadResultDto(); var errors = new List<string>(); int successRows = 0;
        try
        {
            using var wb = new XLWorkbook(fileStream); var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var allData = (await _uow.ReasonRejects.GetAllAsync()).ToList();
            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var code = ws.Cell(row, 1).GetString().Trim(); var desc = ws.Cell(row, 2).GetString().Trim();
                    if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(desc)) { errors.Add($"Row {row}: ReasonCode dan Description wajib."); continue; }
                    var existing = allData.FirstOrDefault(x => x.ReasonCode.Equals(code, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) {
                        var newItem = new ReasonReject { ReasonCode = code, Description = desc, IsActive = true };
                        await _uow.ReasonRejects.AddAsync(newItem); allData.Add(newItem);
                    } else {
                        existing.Description = desc; await _uow.ReasonRejects.UpdateAsync(existing);
                    }
                    successRows++;
                }
                catch (Exception ex) { errors.Add($"Row {row}: {ex.Message}"); }
            }
            await _uow.SaveChangesAsync();
            result.Success = successRows > 0 || errors.Count == 0; result.SuccessRows = successRows; result.FailedRows = errors.Count; result.Errors = errors; result.Message = "Import selesai";
        }
        catch (Exception ex) { result.Success = false; result.Message = "Error: " + ex.Message; }
        return result;
    }

    // ==============================================
    // USERS
    // ==============================================
    public byte[] GenerateUserTemplate()
    {
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("Users");
        ws.Cell(1, 1).Value = "NIK"; ws.Cell(1, 2).Value = "Username"; ws.Cell(1, 3).Value = "FullName"; 
        ws.Cell(1, 4).Value = "RoleName"; ws.Cell(1, 5).Value = "Email"; ws.Cell(1, 6).Value = "Password (Optional)";
        ws.Row(1).Style.Font.Bold = true; ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<byte[]> ExportUsersAsync()
    {
        var data = await _uow.Users.GetWithRolesAsync();
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("Users");
        ws.Cell(1, 1).Value = "NIK"; ws.Cell(1, 2).Value = "Username"; ws.Cell(1, 3).Value = "FullName";
        ws.Cell(1, 4).Value = "RoleName"; ws.Cell(1, 5).Value = "Email"; ws.Cell(1, 6).Value = "IsActive";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var item in data)
        {
            ws.Cell(row, 1).Value = item.NIK; ws.Cell(row, 2).Value = item.Username; ws.Cell(row, 3).Value = item.FullName;
            ws.Cell(row, 4).Value = item.Role?.RoleName ?? ""; ws.Cell(row, 5).Value = item.Email; ws.Cell(row, 6).Value = item.IsActive ? "Yes" : "No";
            row++;
        }
        ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<UploadResultDto> ImportUsersAsync(Stream fileStream)
    {
        var result = new UploadResultDto(); var errors = new List<string>(); int successRows = 0;
        try
        {
            using var wb = new XLWorkbook(fileStream); var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var allData = (await _uow.Users.GetAllAsync()).ToList();
            var roles = (await _uow.Roles.GetAllAsync()).ToList();
            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var nik = ws.Cell(row, 1).GetString().Trim(); var username = ws.Cell(row, 2).GetString().Trim();
                    var fullname = ws.Cell(row, 3).GetString().Trim(); var roleName = ws.Cell(row, 4).GetString().Trim();
                    var email = ws.Cell(row, 5).GetString().Trim(); var password = ws.Cell(row, 6).GetString().Trim();

                    if (string.IsNullOrEmpty(nik) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(fullname) || string.IsNullOrEmpty(roleName)) { 
                        errors.Add($"Row {row}: NIK, Username, FullName, RoleName wajib."); continue; 
                    }
                    
                    var role = roles.FirstOrDefault(r => r.RoleName.Equals(roleName, StringComparison.OrdinalIgnoreCase));
                    if(role == null) { errors.Add($"Row {row}: Role '{roleName}' tidak ditemukan."); continue; }

                    var existing = allData.FirstOrDefault(x => x.NIK.Equals(nik, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) {
                        var checkU = allData.FirstOrDefault(x => x.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
                        if(checkU != null) { errors.Add($"Row {row}: Username '{username}' sudah digunakan NIK lain."); continue; }

                        var newItem = new User { 
                            NIK = nik, Username = username, FullName = fullname, RoleId = role.Id, Email = email, IsActive = true,
                            PasswordHash = BCrypt.Net.BCrypt.HashPassword(string.IsNullOrEmpty(password) ? "Operator@123" : password)
                        };
                        await _uow.Users.AddAsync(newItem); allData.Add(newItem);
                    } else {
                        existing.Username = username; existing.FullName = fullname; existing.RoleId = role.Id; existing.Email = email;
                        if(!string.IsNullOrEmpty(password)) existing.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password);
                        await _uow.Users.UpdateAsync(existing);
                    }
                    successRows++;
                }
                catch (Exception ex) { errors.Add($"Row {row}: {ex.Message}"); }
            }
            await _uow.SaveChangesAsync();
            result.Success = successRows > 0 || errors.Count == 0; result.SuccessRows = successRows; result.FailedRows = errors.Count; result.Errors = errors; result.Message = "Import selesai";
        }
        catch (Exception ex) { result.Success = false; result.Message = "Error: " + ex.Message; }
        return result;
    }

    // ==============================================
    // MAN POWER
    // ==============================================
    public byte[] GenerateManPowerTemplate()
    {
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("ManPower");
        ws.Cell(1, 1).Value = "NPK"; ws.Cell(1, 2).Value = "Name";
        ws.Row(1).Style.Font.Bold = true; ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<byte[]> ExportManPowerAsync()
    {
        var data = await _uow.ManPowers.GetAllAsync();
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("ManPower");
        ws.Cell(1, 1).Value = "NPK"; ws.Cell(1, 2).Value = "Name"; ws.Cell(1, 3).Value = "IsActive";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var item in data)
        {
            ws.Cell(row, 1).Value = item.NPK; ws.Cell(row, 2).Value = item.Name; ws.Cell(row, 3).Value = item.IsActive ? "Yes" : "No";
            row++;
        }
        ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<UploadResultDto> ImportManPowerAsync(Stream fileStream)
    {
        var result = new UploadResultDto(); var errors = new List<string>(); int successRows = 0;
        try
        {
            using var wb = new XLWorkbook(fileStream); var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var allData = (await _uow.ManPowers.GetAllAsync()).ToList();
            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var npk = ws.Cell(row, 1).GetString().Trim(); var name = ws.Cell(row, 2).GetString().Trim();
                    if (string.IsNullOrEmpty(npk) || string.IsNullOrEmpty(name)) { errors.Add($"Row {row}: NPK dan Name wajib diisi."); continue; }
                    var existing = allData.FirstOrDefault(x => x.NPK.Equals(npk, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) {
                        var newItem = new ManPower { NPK = npk, Name = name, IsActive = true };
                        await _uow.ManPowers.AddAsync(newItem); allData.Add(newItem);
                    } else {
                        existing.Name = name; await _uow.ManPowers.UpdateAsync(existing);
                    }
                    successRows++;
                }
                catch (Exception ex) { errors.Add($"Row {row}: {ex.Message}"); }
            }
            await _uow.SaveChangesAsync();
            result.Success = successRows > 0 || errors.Count == 0; result.SuccessRows = successRows; result.FailedRows = errors.Count; result.Errors = errors; result.Message = "Import selesai";
        }
        catch (Exception ex) { result.Success = false; result.Message = "Error: " + ex.Message; }
        return result;
    }

    // ==============================================
    // ITEM MAPPING
    // ==============================================
    public byte[] GenerateItemMappingTemplate()
    {
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("ItemMappings");
        ws.Cell(1, 1).Value = "PartNo"; ws.Cell(1, 2).Value = "KanbanCode"; ws.Cell(1, 3).Value = "Description";
        ws.Row(1).Style.Font.Bold = true; ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<byte[]> ExportItemMappingsAsync()
    {
        var data = await _uow.ItemMappings.GetAllAsync();
        var items = await _uow.Items.GetWithDetailsAsync();
        using var wb = new XLWorkbook(); var ws = wb.Worksheets.Add("ItemMappings");
        ws.Cell(1, 1).Value = "VIN"; ws.Cell(1, 2).Value = "PartNo"; ws.Cell(1, 3).Value = "PartName";
        ws.Cell(1, 4).Value = "KanbanCode"; ws.Cell(1, 5).Value = "Description";
        ws.Row(1).Style.Font.Bold = true;
        int row = 2;
        foreach (var mapping in data)
        {
            var item = items.FirstOrDefault(i => i.Id == mapping.ItemId);
            ws.Cell(row, 1).Value = item?.VIN ?? ""; 
            ws.Cell(row, 2).Value = item?.ItemCode ?? ""; 
            ws.Cell(row, 3).Value = item?.ItemName ?? ""; 
            ws.Cell(row, 4).Value = mapping.KanbanCode; 
            ws.Cell(row, 5).Value = mapping.Description;
            row++;
        }
        ws.Columns().AdjustToContents(); return GetBytes(wb);
    }
    public async Task<UploadResultDto> ImportItemMappingsAsync(Stream fileStream)
    {
        var result = new UploadResultDto(); var errors = new List<string>(); int successRows = 0;
        try
        {
            using var wb = new XLWorkbook(fileStream); var ws = wb.Worksheets.First();
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? 1;
            var allData = (await _uow.ItemMappings.GetAllAsync()).ToList();
            var items = (await _uow.Items.GetWithDetailsAsync()).ToList();
            
            // Check headers to identify columns
            int partNoCol = 1; int kanbanCol = 2; int descCol = 3;
            if (ws.Cell(1, 1).GetString().Contains("VIN", StringComparison.OrdinalIgnoreCase)) {
                partNoCol = 2; kanbanCol = 4; descCol = 5; // Support exported format: VIN, PartNo, PartName, KanbanCode, Description
            }

            for (int row = 2; row <= lastRow; row++)
            {
                try
                {
                    var partNo = ws.Cell(row, partNoCol).GetString().Trim(); 
                    var kanban = ws.Cell(row, kanbanCol).GetString().Trim(); 
                    var desc = ws.Cell(row, descCol).GetString().Trim();
                    
                    if (string.IsNullOrEmpty(partNo) || string.IsNullOrEmpty(kanban)) { errors.Add($"Row {row}: PartNo dan KanbanCode wajib diisi."); continue; }
                    
                    var item = items.FirstOrDefault(i => i.ItemCode.Equals(partNo, StringComparison.OrdinalIgnoreCase));
                    if (item == null) { errors.Add($"Row {row}: Item dengan PartNo '{partNo}' tidak ditemukan."); continue; }

                    var existing = allData.FirstOrDefault(x => x.KanbanCode.Equals(kanban, StringComparison.OrdinalIgnoreCase));
                    if (existing == null) {
                        var newMapping = new ItemMapping { ItemId = item.Id, KanbanCode = kanban, Description = desc, IsActive = true };
                        await _uow.ItemMappings.AddAsync(newMapping); allData.Add(newMapping);
                    } else {
                        existing.ItemId = item.Id; existing.Description = desc; await _uow.ItemMappings.UpdateAsync(existing);
                    }
                    successRows++;
                }
                catch (Exception ex) { errors.Add($"Row {row}: {ex.Message}"); }
            }
            await _uow.SaveChangesAsync();
            result.Success = successRows > 0 || errors.Count == 0; result.SuccessRows = successRows; result.FailedRows = errors.Count; result.Errors = errors; result.Message = "Import selesai";
        }
        catch (Exception ex) { result.Success = false; result.Message = "Error: " + ex.Message; }
        return result;
    }
}
