namespace WarehouseGate.Mobile.Services;

public static class TimeFormat
{
    // "waiting 12m" / "waiting 2h 5m" style relative-time captions, computed client-side from
    // timestamps already present on the job DTOs.
    public static string Since(DateTime utcTimestamp)
    {
        var elapsed = DateTime.UtcNow - utcTimestamp;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed.TotalMinutes < 1)
        {
            return "just now";
        }

        if (elapsed.TotalHours < 1)
        {
            return $"{(int)elapsed.TotalMinutes}m";
        }

        return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
    }

    // "Xh Ym" style caption for a fixed, already-elapsed span (e.g. total time a completed job
    // took), as opposed to Since's "relative to now" framing.
    public static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
        {
            span = TimeSpan.Zero;
        }

        return span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m" : $"{(int)span.TotalHours}h {span.Minutes}m";
    }
}
