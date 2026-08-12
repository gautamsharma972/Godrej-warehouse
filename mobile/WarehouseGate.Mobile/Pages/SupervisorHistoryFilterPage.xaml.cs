namespace WarehouseGate.Mobile.Pages;

public sealed record SupervisorHistoryFilterResult(string? VehicleNumber, string? OrderNumber, DateTime? Date);

public partial class SupervisorHistoryFilterPage : ContentPage
{
    private readonly Action<SupervisorHistoryFilterResult?> _onResult;
    private bool _resultSent;

    public SupervisorHistoryFilterPage(
        bool outward,
        string? vehicleNumber,
        string? orderNumber,
        DateTime? date,
        Action<SupervisorHistoryFilterResult?> onResult)
    {
        InitializeComponent();
        _onResult = onResult;
        OrderLabel.Text = outward ? "DO NUMBER" : "PO NUMBER";
        VehicleEntry.Text = vehicleNumber;
        OrderEntry.Text = orderNumber;
        FilterDatePicker.Date = date ?? DateTime.Today;
        UseDateSwitch.IsToggled = date.HasValue;
        UpdateDateField();
    }

    private void OnDateFilterToggled(object? sender, ToggledEventArgs e) => UpdateDateField();

    private void UpdateDateField()
    {
        DateField.IsEnabled = UseDateSwitch.IsToggled;
        DateField.Opacity = UseDateSwitch.IsToggled ? 1 : 0.45;
    }

    private async void OnApplyClicked(object? sender, EventArgs e)
    {
        if (_resultSent)
        {
            return;
        }

        _resultSent = true;
        _onResult(new SupervisorHistoryFilterResult(
            Clean(VehicleEntry.Text),
            Clean(OrderEntry.Text),
            UseDateSwitch.IsToggled ? FilterDatePicker.Date : null));
        await Navigation.PopModalAsync();
    }

    private async void OnResetClicked(object? sender, EventArgs e)
    {
        if (_resultSent)
        {
            return;
        }

        _resultSent = true;
        _onResult(new SupervisorHistoryFilterResult(null, null, null));
        await Navigation.PopModalAsync();
    }

    private async void OnCancelClicked(object? sender, EventArgs e)
    {
        if (_resultSent)
        {
            return;
        }

        _resultSent = true;
        _onResult(null);
        await Navigation.PopModalAsync();
    }

    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
