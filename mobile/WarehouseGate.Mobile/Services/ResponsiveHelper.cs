namespace WarehouseGate.Mobile.Services;

// Width-based (not device-idiom-based) responsive breakpoint, so behavior is correct both for
// actual tablets and for the Windows target being resized, and doesn't misclassify large phones.
public static class ResponsiveHelper
{
    public const double TabletBreakpoint = 700;

    public static bool IsWide(double width) => width >= TabletBreakpoint;
}
