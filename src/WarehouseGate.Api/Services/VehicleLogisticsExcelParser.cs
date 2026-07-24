using ClosedXML.Excel;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Domain;

namespace WarehouseGate.Api.Services;

// Pure function over a stream + the caller's in-scope warehouses - no DB access itself, so it's
// easy to reason about/test independently of EF Core. Columns are matched by header text (case-
// insensitive), not position, since real-world spreadsheets drift in column order.
public static class VehicleLogisticsExcelParser
{
    private static readonly string[] ExpectedHeaders =
    {
        "veh number", "po number", "inward transaction id", "transporter name", "driver name",
        "driver ph. number", "vehicle type", "sku", "sku code", "box quantity",
        "from warehouse id", "from warehouse name", "to warehouse id", "to warehouse name",
        "departure date", "eta datetime"
    };

    // allWarehouses is the full master list (not region-scoped) - either side of a real dispatch
    // can legitimately sit outside the uploader's own region (e.g. West region warehouse shipping
    // to a North region CFA). Row-level access is instead enforced by requiring at least one side
    // (From or To) to fall in callerRegionId.
    public static (List<VehicleLogisticsRecord> Created, List<VehicleLogisticsUploadRowErrorDto> Errors) Parse(
        Stream stream, string createdByUserId, IReadOnlyCollection<Warehouse> allWarehouses, int? callerRegionId)
    {
        var created = new List<VehicleLogisticsRecord>();
        var errors = new List<VehicleLogisticsUploadRowErrorDto>();

        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.First();
        var headerRow = worksheet.Row(1);
        var lastColumn = headerRow.LastCellUsed()?.Address.ColumnNumber ?? 0;

        var columnIndexByHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var col = 1; col <= lastColumn; col++)
        {
            var header = headerRow.Cell(col).GetString().Trim();
            if (!string.IsNullOrEmpty(header))
            {
                columnIndexByHeader[header] = col;
            }
        }

        var missingHeaders = ExpectedHeaders.Where(h => !columnIndexByHeader.ContainsKey(h)).ToList();
        if (missingHeaders.Count > 0)
        {
            errors.Add(new VehicleLogisticsUploadRowErrorDto(1, $"Missing column(s): {string.Join(", ", missingHeaders)}"));
            return (created, errors);
        }

        string Get(IXLRow row, string header) => row.Cell(columnIndexByHeader[header]).GetString().Trim();

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty())
            {
                continue;
            }

            var vehicleNumber = Get(row, "veh number");
            var sku = Get(row, "sku");
            if (string.IsNullOrWhiteSpace(vehicleNumber) || string.IsNullOrWhiteSpace(sku))
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "Vehicle number and SKU are required."));
                continue;
            }

            var fromWarehouse = ResolveWarehouse(Get(row, "from warehouse id"), Get(row, "from warehouse name"), allWarehouses);
            if (fromWarehouse is null)
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "From warehouse not found in the warehouse master."));
                continue;
            }

            var toWarehouse = ResolveWarehouse(Get(row, "to warehouse id"), Get(row, "to warehouse name"), allWarehouses);
            if (toWarehouse is null)
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "To warehouse not found in the warehouse master."));
                continue;
            }

            if (fromWarehouse.Id == toWarehouse.Id)
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "From and To warehouse cannot be the same."));
                continue;
            }

            if (callerRegionId is not null && fromWarehouse.RegionId != callerRegionId && toWarehouse.RegionId != callerRegionId)
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "Neither the From nor the To warehouse is in your region."));
                continue;
            }

            var boxQuantityText = Get(row, "box quantity");
            if (!int.TryParse(boxQuantityText, out var boxQuantity))
            {
                boxQuantity = 0;
            }

            created.Add(new VehicleLogisticsRecord
            {
                VehicleNumber = vehicleNumber,
                PoNumber = NullIfEmpty(Get(row, "po number")),
                InwardTransactionId = NullIfEmpty(Get(row, "inward transaction id")),
                TransporterName = NullIfEmpty(Get(row, "transporter name")),
                DriverName = NullIfEmpty(Get(row, "driver name")),
                DriverPhone = NullIfEmpty(Get(row, "driver ph. number")),
                VehicleType = NullIfEmpty(Get(row, "vehicle type")),
                Sku = sku,
                SkuCode = NullIfEmpty(Get(row, "sku code")),
                BoxQuantity = boxQuantity,
                DepartureDate = ParseDateOrNull(row.Cell(columnIndexByHeader["departure date"])),
                EtaDateTime = ParseDateOrNull(row.Cell(columnIndexByHeader["eta datetime"])),
                FromWarehouseId = fromWarehouse.Id,
                ToWarehouseId = toWarehouse.Id,
                Status = VehicleLogisticsStatus.InTransit,
                CreatedByUserId = createdByUserId,
                CreatedAtUtc = DateTime.UtcNow
            });
        }

        return (created, errors);
    }

    private static Warehouse? ResolveWarehouse(string idText, string nameText, IReadOnlyCollection<Warehouse> allWarehouses)
    {
        if (int.TryParse(idText, out var id))
        {
            var byId = allWarehouses.FirstOrDefault(w => w.Id == id);
            if (byId is not null)
            {
                return byId;
            }
        }

        if (!string.IsNullOrWhiteSpace(nameText))
        {
            return allWarehouses.FirstOrDefault(w => string.Equals(w.Name, nameText, StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }

    private static DateTime? ParseDateOrNull(IXLCell cell)
    {
        if (cell.TryGetValue(out DateTime dateValue))
        {
            return dateValue;
        }

        var text = cell.GetString().Trim();
        return DateTime.TryParse(text, out var parsed) ? parsed : null;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
