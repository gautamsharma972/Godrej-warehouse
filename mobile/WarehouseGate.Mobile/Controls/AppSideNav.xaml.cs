using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Controls;

public partial class AppSideNav : ContentView
{
    public AppSideNav()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
        {
            Shell.Current.Navigated += OnShellNavigated;
        }

        UpdateVisibleNav();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (Shell.Current is not null)
        {
            Shell.Current.Navigated -= OnShellNavigated;
        }
    }

    private void OnShellNavigated(object? sender, ShellNavigatedEventArgs e) => UpdateVisibleNav();

    private void UpdateVisibleNav()
    {
        SecurityNav.IsVisible = Session.IsSecurity;
        SupervisorNav.IsVisible = Session.IsSupervisor;
    }
}
