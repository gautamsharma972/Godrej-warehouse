using ClosedXML.Excel;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Domain;

namespace WarehouseGate.Api.Services;

// Pure function over a stream + the caller's existing product catalog - no DB access itself,
// mirroring VehicleLogisticsExcelParser's shape. Columns are matched by header text (case-
// insensitive), not position, since real-world spreadsheets drift in column order.
public static class ProductExcelParser
{
    private static readonly string[] ExpectedHeaders =
    {
        "sku code", "name", "weight (kg)", "length (cm)", "width (cm)", "height (cm)",
        "category", "stackable", "max stack layers"
    };

    // Deliberately no "color" column - colors are never read from the sheet, only ever computed
    // via ProductColorAssigner (see its own header comment - shared with the manual Add/Edit form).
    public static (List<Product> Created, int DuplicateCount, List<ProductUploadRowErrorDto> Errors) Parse(
        Stream stream, IReadOnlyCollection<Product> existingProducts)
    {
        var created = new List<Product>();
        var errors = new List<ProductUploadRowErrorDto>();
        var duplicateCount = 0;

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
            errors.Add(new ProductUploadRowErrorDto(1, $"Missing column(s): {string.Join(", ", missingHeaders)}"));
            return (created, duplicateCount, errors);
        }

        string Get(IXLRow row, string header) =>
            columnIndexByHeader.TryGetValue(header, out var col) ? row.Cell(col).GetString().Trim() : string.Empty;

        // Case-insensitive SKU/name uniqueness, checked against both the existing catalog AND
        // every row already accepted earlier in this same upload - two rows in one file with the
        // same SKU or name are just as much a duplicate as one that collides with an existing row.
        var seenSkuCodes = new HashSet<string>(
            existingProducts.Select(p => p.SkuCode), StringComparer.OrdinalIgnoreCase);
        var seenNames = new HashSet<string>(
            existingProducts.Select(p => p.Name), StringComparer.OrdinalIgnoreCase);

        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 1;
        for (var rowNumber = 2; rowNumber <= lastRow; rowNumber++)
        {
            var row = worksheet.Row(rowNumber);
            if (row.IsEmpty())
            {
                continue;
            }

            var skuCode = Get(row, "sku code");
            if (string.IsNullOrWhiteSpace(skuCode))
            {
                errors.Add(new ProductUploadRowErrorDto(rowNumber, "SKU Code is required."));
                continue;
            }

            var name = Get(row, "name");
            if (string.IsNullOrWhiteSpace(name))
            {
                errors.Add(new ProductUploadRowErrorDto(rowNumber, "Name is required."));
                continue;
            }

            if (!seenSkuCodes.Add(skuCode))
            {
                duplicateCount++;
                errors.Add(new ProductUploadRowErrorDto(rowNumber, $"Duplicate SKU Code '{skuCode}' - already exists or repeated in this file."));
                continue;
            }

            if (!seenNames.Add(name))
            {
                duplicateCount++;
                errors.Add(new ProductUploadRowErrorDto(rowNumber, $"Duplicate product Name '{name}' - already exists or repeated in this file."));
                continue;
            }

            // Blank Category defaults to Medium, and an unrecognized value falls back to Medium
            // rather than rejecting the row - the same "never leave a physical property unset"
            // reasoning as Stackable below.
            var categoryText = Get(row, "category");
            var category = string.IsNullOrWhiteSpace(categoryText)
                ? WeightCategory.Medium
                : Enum.TryParse<WeightCategory>(categoryText, ignoreCase: true, out var parsedCategory)
                    ? parsedCategory
                    : WeightCategory.Medium;

            // Blank Stackable defaults to true (matches Product.IsStackable's own entity default).
            // Accepts the common spreadsheet spellings for yes/no, not just "true"/"false".
            var stackableText = Get(row, "stackable");
            var isStackable = string.IsNullOrWhiteSpace(stackableText) || ParseYesNo(stackableText) is not false;

            // Blank Max Stack Layers follows from Stackable: 99 (the entity's own default) when
            // stackable, 1 when not - but an explicitly given value is always honored as-is, even
            // if it seems to contradict Stackable, since the manual Add/Edit form doesn't enforce
            // that relationship either.
            var maxStackLayersText = Get(row, "max stack layers");
            var maxStackLayers = string.IsNullOrWhiteSpace(maxStackLayersText)
                ? (isStackable ? 99 : 1)
                : int.TryParse(maxStackLayersText, out var parsedLayers) && parsedLayers > 0
                    ? parsedLayers
                    : (isStackable ? 99 : 1);

            // Weight/dimensions have no natural "default value" the way a category or a yes/no flag
            // does, so a blank cell is just 0 - a real value can always be filled in later via Edit.
            var weightKg = ParseDecimalOrZero(Get(row, "weight (kg)"));
            var lengthCm = ParseDecimalOrZero(Get(row, "length (cm)"));
            var widthCm = ParseDecimalOrZero(Get(row, "width (cm)"));
            var heightCm = ParseDecimalOrZero(Get(row, "height (cm)"));

            created.Add(new Product
            {
                Name = name,
                SkuCode = skuCode,
                WeightKg = weightKg,
                LengthCm = lengthCm,
                WidthCm = widthCm,
                HeightCm = heightCm,
                Category = category,
                IsStackable = isStackable,
                MaxStackLayers = maxStackLayers,
                ColorHex = ProductColorAssigner.AutoColorFor(skuCode)
            });
        }

        return (created, duplicateCount, errors);
    }

    private static bool? ParseYesNo(string text) => text.Trim().ToLowerInvariant() switch
    {
        "yes" or "y" or "true" or "1" => true,
        "no" or "n" or "false" or "0" => false,
        _ => null
    };

    private static decimal ParseDecimalOrZero(string text) =>
        decimal.TryParse(text, out var value) ? value : 0m;
}
