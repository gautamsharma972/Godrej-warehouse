namespace WarehouseGate.Api.Services;

// Shared by every path that can create/update a Product (the manual Add/Edit dialog's
// AdminController endpoints and ProductExcelParser's bulk import) - color is never a field the
// user fills in themselves, so anywhere a product would otherwise end up with no color, this is
// the one place that decides what it gets instead.
public static class ProductColorAssigner
{
    private static readonly string[] Palette =
        { "#4f7cff", "#ff9f43", "#26c281", "#e0568c", "#8e6cff", "#ffcf44", "#3fbfbf", "#ff6b6b" };

    // Hashed rather than assigned by row/creation order, so the same SKU always gets the same
    // color no matter when or how many times it's saved (re-importing the same file, or editing
    // and re-saving a product, would otherwise shift colors around if this were index-based).
    public static string AutoColorFor(string skuCode)
    {
        var hash = 0;
        foreach (var ch in skuCode.ToUpperInvariant())
        {
            hash = hash * 31 + ch;
        }
        var index = Math.Abs(hash) % Palette.Length;
        return Palette[index];
    }
}
