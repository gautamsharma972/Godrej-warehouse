namespace WarehouseGate.Domain;

public class Warehouse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public WarehouseType WarehouseType { get; set; }

    public int RegionId { get; set; }
    public Region? Region { get; set; }

    public int StateId { get; set; }
    public State? State { get; set; }

    public int CityId { get; set; }
    public City? City { get; set; }

    public int CountryId { get; set; }
    public Country? Country { get; set; }

    // Operational settings feeding the dashboard KPIs - all optional; null falls back to the
    // system defaults (SLA 30 min, 10 dock hours/day, 8 shift hours/day) that used to be the
    // only, hardcoded values. See DashboardAnalyticsService.
    public int? SlaTargetMinutes { get; set; }
    public decimal? DockOperatingHoursPerDay { get; set; }
    public decimal? ShiftHoursPerDay { get; set; }
}
