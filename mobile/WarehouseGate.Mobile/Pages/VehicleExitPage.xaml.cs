using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class VehicleExitPage : ContentPage
{
    // (Display label, OutwardPhotoType, max photo count) - see OutwardService.MaxGateArrivalPhotos
    // for the server-side caps this mirrors.
    private static readonly (string Label, string Type, int Max)[] ArrivalPhotoCategories =
    {
        ("Vehicle", "VehicleAtGate", 5),
        ("Driver", "Driver", 3),
        ("Vehicle RC", "VehicleRc", 2),
        ("Driving Licence", "DrivingLicense", 2)
    };

    private string? _selectedOutwardTransporter;
    private readonly Dictionary<string, List<string>> _pendingArrivalPhotosByType =
        ArrivalPhotoCategories.ToDictionary(c => c.Type, c => new List<string>());
    private bool? _isWide;
    private List<VehicleOption> _outwardVehicleOptions = new();

    public VehicleExitPage()
    {
        InitializeComponent();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        var wide = ResponsiveHelper.IsWide(width);
        if (_isWide == wide)
        {
            return;
        }

        _isWide = wide;
        ApplyResponsiveLayout(wide);
    }

    // Tablet: dispatch-details card and gate-photo card sit side by side, 7:5. Phone: stacked full-width.
    private void ApplyResponsiveLayout(bool wide)
    {
        if (wide)
        {
            FormCardsGrid.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto) };
            FormCardsGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(7, GridUnitType.Star)),
                new ColumnDefinition(new GridLength(5, GridUnitType.Star))
            };
            Grid.SetRow(DispatchDetailsCard, 0);
            Grid.SetColumn(DispatchDetailsCard, 0);
            Grid.SetRow(GatePhotoCard, 0);
            Grid.SetColumn(GatePhotoCard, 1);
        }
        else
        {
            FormCardsGrid.ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star) };
            FormCardsGrid.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)
            };
            Grid.SetRow(DispatchDetailsCard, 0);
            Grid.SetColumn(DispatchDetailsCard, 0);
            Grid.SetRow(GatePhotoCard, 1);
            Grid.SetColumn(GatePhotoCard, 0);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        ShowGateInFormState();

        if (OutwardGatePicker.ItemsSource is null)
        {
            OutwardGatePicker.ItemsSource = Gates;
        }

        var voiceAndScanSupported = SpeechToTextService.IsSupported;
        DispatchOrderNumberMicButton.IsVisible = voiceAndScanSupported;
        DispatchOrderNumberScanButton.IsVisible = voiceAndScanSupported;
        OutwardVehicleNumberMicButton.IsVisible = false;
        OutwardDriverNameMicButton.IsVisible = voiceAndScanSupported;
        OutwardDriverMobileMicButton.IsVisible = voiceAndScanSupported;

        RefreshArrivalPhotoCategories();
        _ = LoadOutwardVehicleOptionsAsync();
    }

    // Real Admin Vehicle Registry - searchable, with free-text entry still available as fallback
    // for a plate not in the registry yet (ApiClient.OutwardGateCheckInAsync creates one on the fly).
    private async Task LoadOutwardVehicleOptionsAsync()
    {
        try
        {
            var vehicles = await ApiClient.GetVehicleRegistryAsync();
            _outwardVehicleOptions = vehicles
                .Select(v => new VehicleOption { VehicleNumber = v.Number, Summary = "Vehicle registry" })
                .ToList();
        }
        catch
        {
            _outwardVehicleOptions = new();
        }
    }

    private async Task<string?> CapturePhotoToLocalCacheAsync()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("Not supported", "This device does not support photo capture.", "OK");
                return null;
            }

            return await PhotoCapture.CaptureAndSaveAsync();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Camera error", ex.Message, "OK");
            return null;
        }
    }

    private void ShowGateInFormState()
    {
        GateInFormSection.IsVisible = true;
        GateInSuccessSection.IsVisible = false;
    }

    private void ShowGateInSuccessState()
    {
        GateInFormSection.IsVisible = false;
        GateInSuccessSection.IsVisible = true;
    }

    private static readonly string[] Gates = { "Gate 1", "Gate 2", "Gate 3", "Gate 4", "Gate 5", "Gate 6" };

    private void OnUppercaseTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not Entry entry)
        {
            return;
        }

        var upper = e.NewTextValue?.ToUpperInvariant() ?? string.Empty;
        if (entry.Text != upper)
        {
            entry.Text = upper;
            return;
        }

        if (string.IsNullOrWhiteSpace(upper))
        {
            return;
        }

        if (entry == DispatchOrderNumberEntry)
        {
            ClearFieldValidation(DispatchOrderNumberBorder, DispatchOrderNumberErrorIcon, DispatchOrderNumberErrorLabel);
        }
        else if (entry == OutwardVehicleNumberEntry)
        {
            ClearFieldValidation(OutwardVehicleNumberBorder, OutwardVehicleNumberErrorIcon, OutwardVehicleNumberErrorLabel);
            OutwardVehicleNumberChevron.IsVisible = true;
            OutwardVehicleNumberNotFoundLabel.IsVisible = false;
        }
    }

    private static bool ValidateRequiredField(Entry entry, Border border, Label errorIcon, Label errorLabel)
    {
        var isValid = !string.IsNullOrWhiteSpace(entry.Text);
        border.Stroke = isValid ? Colors.Transparent : (Color)Application.Current!.Resources["StatusException"];
        errorIcon.IsVisible = !isValid;
        errorLabel.IsVisible = !isValid;
        return isValid;
    }

    private static void ClearFieldValidation(Border border, Label errorIcon, Label errorLabel)
    {
        border.Stroke = Colors.Transparent;
        errorIcon.IsVisible = false;
        errorLabel.IsVisible = false;
    }

    private async Task FillViaVoiceAsync(Entry entry)
    {
        var text = await SpeechToTextService.ListenAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            entry.Text = text;
        }
    }

    private async Task ScanIntoAsync(Entry entry)
    {
        var tcs = new TaskCompletionSource<string?>();
        await Navigation.PushModalAsync(new BarcodeScanPage(result => tcs.TrySetResult(result)));
        var scanned = await tcs.Task;
        if (!string.IsNullOrWhiteSpace(scanned))
        {
            entry.Text = scanned;
        }
    }

    private async void OnDispatchOrderNumberMicClicked(object? sender, EventArgs e) => await FillViaVoiceAsync(DispatchOrderNumberEntry);
    private async void OnOutwardVehicleNumberMicClicked(object? sender, EventArgs e) => await FillViaVoiceAsync(OutwardVehicleNumberEntry);
    private async void OnOutwardDriverNameMicClicked(object? sender, EventArgs e) => await FillViaVoiceAsync(OutwardDriverNameEntry);
    private async void OnOutwardDriverMobileMicClicked(object? sender, EventArgs e) => await FillViaVoiceAsync(OutwardDriverMobileEntry);

    private void OnOutwardDriverFieldFocused(object? sender, FocusEventArgs e) =>
        UiHelpers.SetFieldFocus(sender == OutwardDriverNameEntry ? OutwardDriverNameEntryBorder : OutwardDriverMobileEntryBorder, true);

    private void OnOutwardDriverFieldUnfocused(object? sender, FocusEventArgs e) =>
        UiHelpers.SetFieldFocus(sender == OutwardDriverNameEntry ? OutwardDriverNameEntryBorder : OutwardDriverMobileEntryBorder, false);
    private async void OnScanDispatchOrderNumberClicked(object? sender, EventArgs e) => await ScanIntoAsync(DispatchOrderNumberEntry);

    private async void OnOutwardVehicleNumberUnfocused(object? sender, FocusEventArgs e)
    {
        var vehicleNumber = OutwardVehicleNumberEntry.Text?.Trim();
        if (string.IsNullOrWhiteSpace(vehicleNumber))
        {
            return;
        }

        var record = await VehicleLookupService.LookupAsync(vehicleNumber);
        if (record is null)
        {
            OutwardVehicleNumberNotFoundLabel.IsVisible = true;
            return;
        }

        OutwardVehicleNumberNotFoundLabel.IsVisible = false;
        OutwardDriverNameEntry.Text = record.DriverName;
        OutwardDriverMobileEntry.Text = record.DriverMobile;
        SetSelectedOutwardTransporter(record.TransporterName);

        if (!string.IsNullOrWhiteSpace(record.DispatchOrderNumber))
        {
            DispatchOrderNumberEntry.Text = record.DispatchOrderNumber;
            ClearFieldValidation(DispatchOrderNumberBorder, DispatchOrderNumberErrorIcon, DispatchOrderNumberErrorLabel);
        }
    }

    private async void OnOutwardVehicleFieldTapped(object? sender, EventArgs e)
    {
        if (_outwardVehicleOptions.Count == 0)
        {
            await LoadOutwardVehicleOptionsAsync();
        }

        if (_outwardVehicleOptions.Count == 0)
        {
            await DisplayAlert("No vehicles found",
                "No vehicles are registered yet. Type the vehicle number manually, or try again later.", "OK");
            return;
        }

        var tcs = new TaskCompletionSource<string?>();
        await Navigation.PushModalAsync(new ExpectedVehiclePickerPage(_outwardVehicleOptions, result => tcs.TrySetResult(result)));
        var selected = await tcs.Task;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            await OnOutwardVehicleSelectedAsync(selected);
        }
    }

    private async Task OnOutwardVehicleSelectedAsync(string vehicleNumber)
    {
        OutwardVehicleNumberEntry.Text = vehicleNumber;
        OutwardVehicleNumberLabel.Text = vehicleNumber;
        OutwardVehicleNumberLabel.TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"];
        ClearFieldValidation(OutwardVehicleNumberBorder, OutwardVehicleNumberErrorIcon, OutwardVehicleNumberErrorLabel);
        OutwardVehicleNumberNotFoundLabel.IsVisible = false;

        var record = await VehicleLookupService.LookupAsync(vehicleNumber);
        if (record is null)
        {
            OutwardVehicleNumberNotFoundLabel.IsVisible = true;
            return;
        }

        OutwardDriverNameEntry.Text = record.DriverName;
        OutwardDriverMobileEntry.Text = record.DriverMobile;
        SetSelectedOutwardTransporter(record.TransporterName);

        if (!string.IsNullOrWhiteSpace(record.DispatchOrderNumber))
        {
            DispatchOrderNumberEntry.Text = record.DispatchOrderNumber;
            ClearFieldValidation(DispatchOrderNumberBorder, DispatchOrderNumberErrorIcon, DispatchOrderNumberErrorLabel);
        }
    }

    private void SetSelectedOutwardTransporter(string? transporter)
    {
        _selectedOutwardTransporter = transporter;
        OutwardTransporterLabel.Text = string.IsNullOrWhiteSpace(transporter) ? "Select transporter" : transporter;
        OutwardTransporterLabel.TextColor = (Color)Application.Current!.Resources[
            string.IsNullOrWhiteSpace(transporter) ? "TextSecondaryLight" : "TextPrimaryLight"];
    }

    private async void OnOutwardTransporterFieldTapped(object? sender, EventArgs e)
    {
        var tcs = new TaskCompletionSource<string?>();
        await Navigation.PushModalAsync(new TransporterPickerPage(result => tcs.TrySetResult(result)));
        var selected = await tcs.Task;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SetSelectedOutwardTransporter(selected);
        }
    }

    // Rebuilds the 4 gate-photo category rows (Vehicle/Driver/Vehicle RC/Driving Licence) from
    // scratch - mirrors JobDetailPage.BuildSkuPhotoRow's "count/max + Add Photo" pattern, just
    // repeated per category instead of per PO line.
    private void RefreshArrivalPhotoCategories()
    {
        ArrivalPhotoCategoriesContainer.Children.Clear();
        foreach (var category in ArrivalPhotoCategories)
        {
            ArrivalPhotoCategoriesContainer.Children.Add(BuildArrivalPhotoCategoryRow(category.Label, category.Type, category.Max));
        }
    }

    private View BuildArrivalPhotoCategoryRow(string label, string type, int max)
    {
        var photos = _pendingArrivalPhotosByType[type];

        var titleLabel = new Label
        {
            Text = $"{label} ({photos.Count}/{max})",
            FontFamily = "PoppinsSemiBold",
            FontSize = 14,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"]
        };

        var addButton = new Button
        {
            Text = "Add Photo",
            IsEnabled = photos.Count < max,
            Style = (Style)Application.Current!.Resources["ChipButton"],
            FontSize = 11,
            ImageSource = new FontImageSource { FontFamily = "FaSolid", Glyph = IconGlyphs.Camera, Color = (Color)Application.Current.Resources["Primary"], Size = 11 }
        };
        addButton.Clicked += async (_, _) => await CaptureArrivalPhotoAsync(type);

        titleLabel.VerticalOptions = LayoutOptions.Center;
        var header = new Grid { ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) } };
        Grid.SetColumn(titleLabel, 0);
        Grid.SetColumn(addButton, 1);
        header.Children.Add(titleLabel);
        header.Children.Add(addButton);

        var row = new VerticalStackLayout { Spacing = 8 };
        row.Children.Add(header);

        if (photos.Count > 0)
        {
            var thumbnails = new HorizontalStackLayout { Spacing = 8 };
            foreach (var path in photos)
            {
                thumbnails.Children.Add(new Border
                {
                    WidthRequest = 56,
                    HeightRequest = 56,
                    StrokeThickness = 0,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                    Content = new Image { Source = ImageSource.FromFile(path), Aspect = Aspect.AspectFill }
                });
            }
            row.Children.Add(thumbnails);
        }

        return row;
    }

    private async Task CaptureArrivalPhotoAsync(string type)
    {
        var localPath = await CapturePhotoToLocalCacheAsync();
        if (localPath is null)
        {
            return;
        }

        _pendingArrivalPhotosByType[type].Add(localPath);
        RefreshArrivalPhotoCategories();
    }

    private static async Task<Location?> TryGetLocationAsync()
    {
        try
        {
            return await Geolocation.Default.GetLocationAsync(new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10)));
        }
        catch
        {
            return null;
        }
    }

    private async void OnGateInSubmitClicked(object? sender, EventArgs e)
    {
        GateInResultBorder.IsVisible = false;

        // Dispatch Order Number is an optional hint now (Office links the vehicle afterward) -
        // only Vehicle Number is still required to check in.
        var vehicleValid = ValidateRequiredField(OutwardVehicleNumberEntry, OutwardVehicleNumberBorder, OutwardVehicleNumberErrorIcon, OutwardVehicleNumberErrorLabel);
        OutwardVehicleNumberChevron.IsVisible = vehicleValid;

        if (!vehicleValid)
        {
            return;
        }

        var vehicleNumber = OutwardVehicleNumberEntry.Text!.Trim();
        var dispatchOrderNumber = string.IsNullOrWhiteSpace(DispatchOrderNumberEntry.Text) ? null : DispatchOrderNumberEntry.Text.Trim();

        GateInSubmitButton.IsEnabled = false;
        GateInSubmitButton.Text = "Gating out...";
        GateInSpinner.IsVisible = true;
        GateInSpinner.IsRunning = true;

        try
        {
            var location = await TryGetLocationAsync();

            var arrival = await ApiClient.OutwardGateCheckInAsync(new OutwardGateCheckInInput
            {
                VehicleNumber = vehicleNumber,
                DriverName = string.IsNullOrWhiteSpace(OutwardDriverNameEntry.Text) ? null : OutwardDriverNameEntry.Text.Trim(),
                DriverMobile = string.IsNullOrWhiteSpace(OutwardDriverMobileEntry.Text) ? null : OutwardDriverMobileEntry.Text.Trim(),
                TransporterName = string.IsNullOrWhiteSpace(_selectedOutwardTransporter) ? null : _selectedOutwardTransporter,
                GateName = OutwardGatePicker.SelectedItem as string,
                GpsLatitude = location?.Latitude,
                GpsLongitude = location?.Longitude,
                DispatchOrderNumber = dispatchOrderNumber
            });

            var uploadFailures = 0;
            foreach (var category in ArrivalPhotoCategories)
            {
                foreach (var localPath in _pendingArrivalPhotosByType[category.Type])
                {
                    try
                    {
                        await ApiClient.UploadOutwardGateArrivalPhotoAsync(arrival.Id, category.Type, localPath);
                    }
                    catch (Exception)
                    {
                        uploadFailures++;
                    }
                }
            }

            ShowGateInSuccessResult(arrival, uploadFailures);
            ResetGateInForm();
        }
        catch (ApiException ex)
        {
            ShowGateInError(ex.Message);
        }
        catch (Exception)
        {
            ShowGateInError("Could not reach the server. Check your connection and try again.");
        }
        finally
        {
            GateInSubmitButton.IsEnabled = true;
            GateInSubmitButton.Text = "Gate-Out Vehicle";
            GateInSpinner.IsVisible = false;
            GateInSpinner.IsRunning = false;
        }
    }

    private void ResetGateInForm()
    {
        DispatchOrderNumberEntry.Text = string.Empty;
        OutwardVehicleNumberEntry.Text = string.Empty;
        OutwardVehicleNumberLabel.Text = "Select vehicle";
        OutwardVehicleNumberLabel.TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"];
        OutwardDriverNameEntry.Text = string.Empty;
        OutwardDriverMobileEntry.Text = string.Empty;
        SetSelectedOutwardTransporter(null);
        OutwardGatePicker.SelectedIndex = -1;

        foreach (var category in ArrivalPhotoCategories)
        {
            _pendingArrivalPhotosByType[category.Type].Clear();
        }
        RefreshArrivalPhotoCategories();

        ClearFieldValidation(DispatchOrderNumberBorder, DispatchOrderNumberErrorIcon, DispatchOrderNumberErrorLabel);
        ClearFieldValidation(OutwardVehicleNumberBorder, OutwardVehicleNumberErrorIcon, OutwardVehicleNumberErrorLabel);
        OutwardVehicleNumberChevron.IsVisible = true;
        OutwardVehicleNumberNotFoundLabel.IsVisible = false;
    }

    private void ShowGateInError(string message)
    {
        GateInResultLabel.Text = message;
        GateInResultIconLabel.Text = IconGlyphs.TriangleExclamation;
        GateInResultBorder.BackgroundColor = (Color)Application.Current!.Resources["StatusExceptionTint"];
        GateInResultLabel.TextColor = (Color)Application.Current!.Resources["StatusException"];
        GateInResultIconLabel.TextColor = (Color)Application.Current!.Resources["StatusException"];
        GateInResultBorder.IsVisible = true;
    }

    private void ShowGateInSuccessResult(OutwardGateArrival arrival, int uploadFailures = 0)
    {
        var driverSuffix = string.IsNullOrWhiteSpace(arrival.DriverName) ? string.Empty : $" - {arrival.DriverName}";
        GateInSuccessDetailLabel.Text = $"{arrival.VehicleNumber}{driverSuffix}\nGate-out recorded - Office will link this vehicle to its dispatch order.";

        GateInSuccessBadgesContainer.Children.Clear();
        if (uploadFailures > 0)
        {
            GateInSuccessBadgesContainer.Children.Add(BuildBadge(
                uploadFailures == 1 ? "1 attachment failed to upload - retry from the job later" : $"{uploadFailures} attachments failed to upload - retry from the job later",
                "StatusException"));
        }

        ShowGateInSuccessState();
    }

    private void OnGateInDoneClicked(object? sender, EventArgs e) => ShowGateInFormState();

    private static View BuildBadge(string text, string colorResourceKey)
    {
        var color = (Color)Application.Current!.Resources[colorResourceKey];
        return new Border
        {
            Padding = new Thickness(8, 4),
            StrokeThickness = 0,
            BackgroundColor = color,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = new Label { Text = text, TextColor = Colors.White, FontSize = 11, FontFamily = "PoppinsSemiBold" }
        };
    }
}
