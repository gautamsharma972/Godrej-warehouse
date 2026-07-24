namespace WarehouseGate.Web.Services;

// Shared by every admin-table "When" column (SuperAdmin Audit Log, Office Audit Trail, ...) so a
// human scanning the list gets "20th July, 26 at 10:26 AM" instead of a raw yyyy-MM-dd HH:mm stamp.
public static class FriendlyDateFormatter
{
    public static string Format(DateTime utc)
    {
        var day = utc.Day;
        var suffix = (day % 10, day) switch
        {
            (_, 11) or (_, 12) or (_, 13) => "th",
            (1, _) => "st",
            (2, _) => "nd",
            (3, _) => "rd",
            _ => "th"
        };
        return $"{day}{suffix} {utc:MMMM}, {utc:yy} at {utc:h:mm tt}";
    }
}
