namespace WarehouseGate.Mobile.Models;

public class DispatchOrderLine
{
    public int Id { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public string UnitOfMeasure { get; set; } = string.Empty;
    public decimal? WeightKg { get; set; }
    public decimal? LengthCm { get; set; }
    public decimal? WidthCm { get; set; }
    public decimal? HeightCm { get; set; }
    public string? SkuCode { get; set; }
    public string? ColorHex { get; set; }
    public string? DeliveryLocation { get; set; }
}

public class OutwardPhoto
{
    public int Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CapturedAt { get; set; }
}

public class LoadLine
{
    public int Id { get; set; }
    public int DispatchOrderLineId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal OrderedQty { get; set; }
    public decimal LoadedQty { get; set; }
    public int LoadSequence { get; set; }
    public string? Notes { get; set; }
}

public class OutwardDispatchNote
{
    public string DispatchNoteNumber { get; set; } = string.Empty;
    public DateTime GeneratedAt { get; set; }
    public bool IsPartial { get; set; }
}

public class OutwardJob
{
    public int Id { get; set; }
    public string DispatchOrderNumber { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string OutwardTxnNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedTime { get; set; }
    public DateTime? GateInTime { get; set; }
    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public string? TransporterName { get; set; }
    public string? GateName { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
    public DateTime? GateOutTime { get; set; }
    public string? GatePassToken { get; set; }
    public string? AssignedSupervisorUserId { get; set; }
    public string? VehicleNumber { get; set; }
    public string? BayName { get; set; }
    public DateTime? DockInTime { get; set; }
    public DateTime? LoadingStartTime { get; set; }
    public DateTime? DockOutTime { get; set; }
    public string? ExceptionReason { get; set; }
    public string? ExceptionRemarks { get; set; }
    public DateTime? ExceptionReportedAt { get; set; }
    public DateTime? DispatchReadyConfirmedAt { get; set; }
    public decimal? VehicleMaxWeightKg { get; set; }
    public decimal? VehicleLengthCm { get; set; }
    public decimal? VehicleWidthCm { get; set; }
    public decimal? VehicleHeightCm { get; set; }
    public List<DispatchOrderLine> Lines { get; set; } = new();
    public List<OutwardPhoto> Photos { get; set; } = new();
    public List<LoadLine> LoadLines { get; set; } = new();
    public OutwardDispatchNote? DispatchNote { get; set; }
    public int LoadPlanTotalSteps { get; set; }
    public int LoadPlanResolvedSteps { get; set; }

    public string WaitingCaption => Services.TimeFormat.Since(CreatedTime);
    public string SubtitleWithTime => $"{CustomerName} · {(DockOutTime ?? CreatedTime):d MMM, h:mm tt}";

    public string? TimeTrackingCaption => Status switch
    {
        "Loading" when LoadingStartTime.HasValue => $"Loading {Services.TimeFormat.Since(LoadingStartTime.Value)}",
        "Completed" when LoadingStartTime.HasValue && DockOutTime.HasValue =>
            $"Took {Services.TimeFormat.Duration(DockOutTime.Value - LoadingStartTime.Value)}",
        _ => null
    };
    public bool HasTimeTrackingCaption => TimeTrackingCaption is not null;

    public double LoadingCompletionProgress
    {
        get
        {
            if (Status == "Completed")
            {
                return 1;
            }

            // The Plan & Load flow (Confirm Loading groups) is the source of truth once a
            // load plan exists - it reflects real-time Start/Loaded/Mismatch/Short/Skip
            // actions. Only fall back to the legacy LoadLines total for older jobs that
            // never went through Plan & Load.
            if (LoadPlanTotalSteps > 0)
            {
                return Math.Clamp((double)LoadPlanResolvedSteps / LoadPlanTotalSteps, 0, 1);
            }

            var totalQty = LoadLines.Count > 0
                ? LoadLines.Sum(l => l.OrderedQty)
                : Lines.Sum(l => l.OrderedQty);

            if (totalQty <= 0)
            {
                return 0;
            }

            var loadedQty = LoadLines.Sum(l => l.LoadedQty);
            return Math.Clamp((double)(loadedQty / totalQty), 0, 1);
        }
    }

    public string LoadingCompletionPercentText => $"{Math.Round(LoadingCompletionProgress * 100)}%";
}

public class LoadLineInput
{
    public int DispatchOrderLineId { get; set; }
    public decimal LoadedQty { get; set; }
    public int LoadSequence { get; set; }
    public string? Notes { get; set; }
}

public class OutwardGateCheckInInput
{
    public string DispatchOrderNumber { get; set; } = string.Empty;
    public string VehicleNumber { get; set; } = string.Empty;
    public string? DriverName { get; set; }
    public string? DriverMobile { get; set; }
    public string? TransporterName { get; set; }
    public string? GateName { get; set; }
    public double? GpsLatitude { get; set; }
    public double? GpsLongitude { get; set; }
}
