using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Controls;

public partial class AppSideNav : ContentView
{
    private string? _renderedRole;

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
        var role = Session.IsSecurity ? "Security" : Session.IsSupervisor ? "Supervisor" : null;
        if (role == _renderedRole)
        {
            return;
        }

        _renderedRole = role;
        NavHost.Children.Clear();

        if (role == "Security")
        {
            NavHost.Children.Add(new SecuritySideNav());
        }
        else if (role == "Supervisor")
        {
            NavHost.Children.Add(new SupervisorSideNav());
        }
    }
}
