using ClosedXML.Excel;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Domain;

namespace WarehouseGate.Api.Services;

// Pure function over a stream + the caller's in-scope warehouses - no DB access itself, so it's
// easy to reason about/test independently of EF Core. Columns are matched by header text (case-
// insensitive), not position, since real-world spreadsheets drift in column order.
public static class VehicleLogisticsExcelParser
{
    // The canonical Dispatch Plan upload format: PO Number, SKU, SKU Code, Box Quantity, From/To
    // Warehouse Name, Departure Date, ETA Datetime. Vehicle/transporter/driver/vehicle-type/inward-
    // transaction-id are no longer part of the format at all - that data is supplied later by
    // Office's "Tag Vehicle" action (see InwardService.TagVehicleAsync). Still accepted if a legacy
    // file happens to include them (read via the optional-column-safe Get() below), just never
    // required.
    private static readonly string[] ExpectedHeaders =
    {
        "po number", "sku", "sku code", "box quantity", "from warehouse name", "to warehouse name",
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

        // Empty string for any column not present in the sheet at all (vehicle/transporter/driver/
        // type/warehouse-id are optional now and may be entirely absent, not just blank per-row).
        string Get(IXLRow row, string header) =>
            columnIndexByHeader.TryGetValue(header, out var col) ? row.Cell(col).GetString().Trim() : string.Empty;

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty())
            {
                continue;
            }

            var poNumber = Get(row, "po number");
            if (string.IsNullOrWhiteSpace(poNumber))
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "PO Number is required."));
                continue;
            }

            // Either SKU or SKU Code is enough - not both. Whichever is present becomes the record's
            // Sku (the identifier used everywhere downstream, e.g. as the synthesized PO line's
            // ProductName); SKU is preferred when both are given.
            var sku = Get(row, "sku");
            var skuCode = Get(row, "sku code");
            if (string.IsNullOrWhiteSpace(sku) && string.IsNullOrWhiteSpace(skuCode))
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "SKU or SKU Code is required."));
                continue;
            }

            var boxQuantityText = Get(row, "box quantity");
            if (!int.TryParse(boxQuantityText, out var boxQuantity) || boxQuantity <= 0)
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "Box Quantity is required and must be a whole number greater than 0."));
                continue;
            }

            var fromWarehouseName = Get(row, "from warehouse name");
            if (string.IsNullOrWhiteSpace(fromWarehouseName))
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "From Warehouse Name is required."));
                continue;
            }

            var toWarehouseName = Get(row, "to warehouse name");
            if (string.IsNullOrWhiteSpace(toWarehouseName))
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "To Warehouse Name is required."));
                continue;
            }

            var fromWarehouse = ResolveWarehouse(Get(row, "from warehouse id"), fromWarehouseName, allWarehouses);
            if (fromWarehouse is null)
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "From warehouse not found in the warehouse master."));
                continue;
            }

            var toWarehouse = ResolveWarehouse(Get(row, "to warehouse id"), toWarehouseName, allWarehouses);
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

            // Date-only: Logistics plans a day, not a time-of-day, for departure/ETA.
            var departureDate = ParseDateOrNull(row.Cell(columnIndexByHeader["departure date"]))?.Date;
            if (departureDate is null)
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "Departure Date is required and must be a valid date."));
                continue;
            }

            var etaDateTime = ParseDateOrNull(row.Cell(columnIndexByHeader["eta datetime"]))?.Date;
            if (etaDateTime is null)
            {
                errors.Add(new VehicleLogisticsUploadRowErrorDto(rowNumber, "ETA Datetime is required and must be a valid date."));
                continue;
            }

            created.Add(new VehicleLogisticsRecord
            {
                VehicleNumber = NullIfEmpty(Get(row, "veh number")),
                PoNumber = poNumber,
                InwardTransactionId = NullIfEmpty(Get(row, "inward transaction id")),
                TransporterName = NullIfEmpty(Get(row, "transporter name")),
                DriverName = NullIfEmpty(Get(row, "driver name")),
                DriverPhone = NullIfEmpty(Get(row, "driver ph. number")),
                VehicleType = NullIfEmpty(Get(row, "vehicle type")),
                Sku = string.IsNullOrWhiteSpace(sku) ? skuCode : sku,
                SkuCode = NullIfEmpty(skuCode),
                BoxQuantity = boxQuantity,
                DepartureDate = departureDate,
                EtaDateTime = etaDateTime,
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
