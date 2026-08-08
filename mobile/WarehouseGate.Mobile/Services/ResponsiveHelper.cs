using System.Linq;

namespace WarehouseGate.Mobile.Services;

// Width-based (not device-idiom-based) responsive breakpoint, so behavior is correct both for
// actual tablets and for the Windows target being resized, and doesn't misclassify large phones.
public static class ResponsiveHelper
{
    public const double TabletBreakpoint = 700;

    public static bool IsWide(double width) => width >= TabletBreakpoint;

    // Collapses a row of side-by-side cards (stat cards, quick-action cards) into a single
    // stacked column on narrow width, or lays them out across wideColumnCount columns when wide.
    // Same Grid.SetRow/Grid.SetColumn reassignment technique JobDetailPage's
    // ConfigureWorkflowEvidenceLayout already uses.
    public static void ConfigureStackableGrid(Grid grid, bool wide, int wideColumnCount)
    {
        var children = grid.Children.Cast<View>().ToList();
        if (children.Count == 0)
        {
            return;
        }

        if (wide)
        {
            grid.ColumnDefinitions = new ColumnDefinitionCollection(
                Enumerable.Range(0, wideColumnCount).Select(_ => new ColumnDefinition(GridLength.Star)).ToArray());
            grid.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto) };
            for (var i = 0; i < children.Count; i++)
            {
                Grid.SetRow(children[i], 0);
                Grid.SetColumn(children[i], i);
            }
        }
        else
        {
            grid.ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star) };
            grid.RowDefinitions = new RowDefinitionCollection(
                Enumerable.Range(0, children.Count).Select(_ => new RowDefinition(GridLength.Auto)).ToArray());
            for (var i = 0; i < children.Count; i++)
            {
                Grid.SetRow(children[i], i);
                Grid.SetColumn(children[i], 0);
            }
        }
    }
}
