using System.Globalization;

namespace WarehouseGate.Mobile.Converters;

// Maps a raw status/condition string - InwardStatus, OutwardStatus, or MaterialCondition, all
// plain strings on the DTOs - to one semantic color used consistently everywhere it appears.
public class StatusToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? string.Empty;
        var resourceKey = key switch
        {
            "GateIn" or "PickListGenerated" => "StatusAvailable",
            "Assigned" => "StatusAssigned",
            "Docked" or "Docking" or "Inspecting" => "StatusDocked",
            "Loading" => "StatusLoading",
            "Completed" or "Ok" => "StatusSuccess",
            "Damaged" or "Short" or "Excess" or "Mismatch" => "StatusException",
            _ => "StatusNeutral"
        };

        return Application.Current!.Resources[resourceKey];
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Maps raw enum-style status strings to supervisor-friendly display text.
public class StatusToDisplayTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? string.Empty;
        return key switch
        {
            "GateIn" => "Vehicle Arrived",
            "PickListGenerated" => "Pick List Ready",
            "Assigned" => "Assigned",
            "Docked" => "Docked",
            "Inspecting" => "Inspecting",
            "Loading" => "Loading",
            "Completed" => "Completed",
            _ => key
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
