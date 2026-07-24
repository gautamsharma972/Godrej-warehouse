using WarehouseGate.Mobile.Controls;
using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

public partial class SecurityHomePage : ContentPage
{
    private static readonly string[] Gates = { "Gate 1", "Gate 2", "Gate 3", "Gate 4", "Gate 5", "Gate 6" };

    private static readonly (string Display, string Value)[] DocumentTypes =
    {
        ("E-Way Bill", "EWayBill"),
        ("Invoice", "Invoice"),
        ("Lorry Receipt", "LorryReceipt"),
        ("Other", "Other")
    };

    private static readonly (string Display, string Value)[] PhotoSlots =
    {
        ("Front", "VehicleFront"),
        ("Rear", "VehicleRear"),
        ("Left", "VehicleLeft"),
        ("Right", "VehicleRight"),
        ("Seal", "VehicleSeal"),
        ("Damage", "VehicleDamage")
    };

    private readonly List<RecentCheckIn> _recentCheckIns = new();
    private readonly List<(string LocalPath, string Type)> _pendingDocuments = new();
    private readonly Dictionary<string, string> _pendingPhotosBySlot = new();
    private int _sessionCount;
    private int _selectedTab;
    private string? _selectedTransporter;
    private bool? _isWide;
    private int _photoGridColumns = 3;

    private List<ExpectedShipment> _expectedShipments = new();
    private string? _selectedVehicleNumber;
    private readonly List<PoTxnRow> _poTxnRows = new();

    private class PoTxnRow
    {
        public required Border Container { get; init; }
        public required Entry PoEntry { get; init; }
        public required Entry TxnEntry { get; init; }
    }

    private sealed record RecentCheckIn(string VehicleNumber, string PoNumber, DateTime CheckedInAt);

    public SecurityHomePage()
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

    // Tablet: Driver Name/Mobile share a row, Transporter/Gate share a row, Inward Txn/PO Number
    // share a row, and the photo-slot grid goes to 6 columns (one row) instead of 3x2. Phone:
    // unchanged - everything stacks exactly as it did before this pass.
    private void ApplyResponsiveLayout(bool wide)
    {
        SetFieldPairLayout(DriverFieldsGrid, DriverNameSection, DriverMobileSection, wide);
        SetFieldPairLayout(TransporterGateFieldsGrid, TransporterSection, GateSection, wide);

        _photoGridColumns = wide ? 6 : 3;
        RenderPhotoSlotTiles();
    }

    private static void SetFieldPairLayout(Grid container, View first, View second, bool wide)
    {
        if (wide)
        {
            container.RowDefinitions = new RowDefinitionCollection { new RowDefinition(GridLength.Auto) };
            container.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)
            };
            container.ColumnSpacing = 14;
            Grid.SetRow(first, 0);
            Grid.SetColumn(first, 0);
            Grid.SetRow(second, 0);
            Grid.SetColumn(second, 1);
        }
        else
        {
            container.ColumnDefinitions = new ColumnDefinitionCollection { new ColumnDefinition(GridLength.Star) };
            container.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto), new RowDefinition(GridLength.Auto)
            };
            Grid.SetRow(first, 0);
            Grid.SetColumn(first, 0);
            Grid.SetRow(second, 1);
            Grid.SetColumn(second, 0);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Always land back on the form, even if the last visit was left sitting on the success
        // screen (e.g. the user left via the bottom nav's own Home button instead of ours).
        FormSection.IsVisible = true;
        SuccessSection.IsVisible = false;

        if (GatePicker.ItemsSource is null)
        {
            GatePicker.ItemsSource = Gates;
        }

        if (DocumentTypePicker.ItemsSource is null)
        {
            DocumentTypePicker.ItemsSource = DocumentTypes.Select(d => d.Display).ToList();
            DocumentTypePicker.SelectedIndex = 0;
        }

        var voiceAndScanSupported = SpeechToTextService.IsSupported;
        DriverNameMicButton.IsVisible = voiceAndScanSupported;
        DriverMobileMicButton.IsVisible = voiceAndScanSupported;
        RemarksMicButton.IsVisible = voiceAndScanSupported;

        if (_poTxnRows.Count == 0)
        {
            AddPoTxnRow(null, VehicleLookupService.GenerateInwardTxnNumber());
        }

        _ = LoadExpectedShipmentsAsync();
        RenderPhotoSlotTiles();
        UpdateTabVisibility();
    }

    private async Task LoadExpectedShipmentsAsync()
    {
        try
        {
            _expectedShipments = await ApiClient.GetExpectedShipmentsAsync();
        }
        catch (Exception)
        {
            _expectedShipments = new();
        }
    }

    private void OnVehicleTabClicked(object? sender, EventArgs e) => SelectTab(0);
    private void OnDocumentsTabClicked(object? sender, EventArgs e) => SelectTab(1);
    private void OnPhotosTabClicked(object? sender, EventArgs e) => SelectTab(2);
    private void OnNextToDocumentsClicked(object? sender, EventArgs e) => SelectTab(1);
    private void OnNextToPhotosClicked(object? sender, EventArgs e) => SelectTab(2);

    private void SelectTab(int index)
    {
        _selectedTab = index;
        UpdateTabVisibility();
    }

    private void UpdateTabVisibility()
    {
        VehicleTabSection.IsVisible = _selectedTab == 0;
        DocumentsTabSection.IsVisible = _selectedTab == 1;
        PhotosTabSection.IsVisible = _selectedTab == 2;

        var activeColor = (Color)Application.Current!.Resources["Primary"];
        var inactiveTextColor = (Color)Application.Current.Resources["TextSecondaryLight"];

        VehicleTabLabel.TextColor = _selectedTab == 0 ? activeColor : inactiveTextColor;
        SetIndicator(VehicleTabIndicator, _selectedTab == 0, activeColor);

        DocumentsTabLabel.TextColor = _selectedTab == 1 ? activeColor : inactiveTextColor;
        SetIndicator(DocumentsTabIndicator, _selectedTab == 1, activeColor);

        PhotosTabLabel.TextColor = _selectedTab == 2 ? activeColor : inactiveTextColor;
        SetIndicator(PhotosTabIndicator, _selectedTab == 2, activeColor);

        SubmitButton.Text = _selectedTab < 2 ? "Next" : "Gate-In Vehicle";
        SubmitButton.BackgroundColor = _selectedTab < 2
            ? activeColor
            : (Color)Application.Current.Resources["StatusSuccess"];

        RefreshTabErrorBadges();
    }

    private static void SetIndicator(BoxView indicator, bool active, Color activeColor)
    {
        var color = active ? activeColor : Colors.Transparent;
        indicator.Color = color;
        indicator.BackgroundColor = color;
    }

    // Mirrors each required field/section's error state onto its owning tab label, so a user can
    // tell a tab has a problem without having to open it first.
    private void RefreshTabErrorBadges()
    {
        VehicleTabErrorIcon.IsVisible = VehicleNumberErrorLabel.IsVisible;
        DocumentsTabErrorIcon.IsVisible = PoTxnErrorLabel.IsVisible;
    }

    private async Task FillViaVoiceAsync(Entry entry)
    {
        var text = await SpeechToTextService.ListenAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            entry.Text = text;
        }
    }

    private async Task FillViaVoiceAsync(Editor editor)
    {
        var text = await SpeechToTextService.ListenAsync();
        if (!string.IsNullOrWhiteSpace(text))
        {
            editor.Text = text;
        }
    }

    private async void OnDriverNameMicClicked(object? sender, EventArgs e) => await FillViaVoiceAsync(DriverNameEntry);
    private async void OnDriverMobileMicClicked(object? sender, EventArgs e) => await FillViaVoiceAsync(DriverMobileEntry);
    private async void OnRemarksMicClicked(object? sender, EventArgs e) => await FillViaVoiceAsync(RemarksEditor);

    private void OnDriverFieldFocused(object? sender, FocusEventArgs e) =>
        UiHelpers.SetFieldFocus(sender == DriverNameEntry ? DriverNameEntryBorder : DriverMobileEntryBorder, true);

    private void OnDriverFieldUnfocused(object? sender, FocusEventArgs e) =>
        UiHelpers.SetFieldFocus(sender == DriverNameEntry ? DriverNameEntryBorder : DriverMobileEntryBorder, false);

    private void OnRemarksFocused(object? sender, FocusEventArgs e) => UiHelpers.SetFieldFocus(RemarksEditorBorder, true);
    private void OnRemarksUnfocused(object? sender, FocusEventArgs e) => UiHelpers.SetFieldFocus(RemarksEditorBorder, false);

    private async void OnVehicleFieldTapped(object? sender, EventArgs e)
    {
        if (_expectedShipments.Count == 0)
        {
            await DisplayAlert("No expected vehicles",
                "There are no pre-registered inward shipments for your warehouse yet. Ask your Logistics Manager to upload the expected shipment list.", "OK");
            return;
        }

        var options = _expectedShipments
            .GroupBy(s => s.VehicleNumber)
            .Select(g => new VehicleOption { VehicleNumber = g.Key, Summary = BuildVehicleSummary(g.ToList()) })
            .OrderBy(o => o.VehicleNumber)
            .ToList();

        var tcs = new TaskCompletionSource<string?>();
        await Navigation.PushModalAsync(new ExpectedVehiclePickerPage(options, result => tcs.TrySetResult(result)));
        var selected = await tcs.Task;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            await OnVehicleSelectedAsync(selected);
        }
    }

    private static string BuildVehicleSummary(List<ExpectedShipment> shipments)
    {
        var poCount = shipments.Select(s => s.PoNumber).Distinct().Count();
        var etas = shipments.Where(s => s.EtaDateTime.HasValue).Select(s => s.EtaDateTime!.Value).OrderBy(d => d).ToList();
        var etaText = etas.Count == 0 ? "ETA not set" : $"ETA {etas[0]:d MMM, h:mm tt}";
        return poCount <= 1 ? $"PO {shipments[0].PoNumber} · {etaText}" : $"{poCount} POs · {etaText}";
    }

    private async Task OnVehicleSelectedAsync(string vehicleNumber)
    {
        _selectedVehicleNumber = vehicleNumber;
        VehicleNumberLabel.Text = vehicleNumber;
        VehicleNumberLabel.TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"];
        VehicleNumberBorder.Stroke = Colors.Transparent;
        VehicleNumberErrorLabel.IsVisible = false;
        RefreshTabErrorBadges();

        var shipmentsForVehicle = _expectedShipments.Where(s => s.VehicleNumber == vehicleNumber).ToList();

        // Driver/transporter auto-fill: VehicleMaster (the curated plate directory) wins when it
        // has this plate on file; otherwise fall back to whatever the Logistics Manager's own
        // pre-registered shipment already specified for this delivery - that data was sitting
        // right there and was previously being ignored for these three fields.
        var record = await VehicleLookupService.LookupAsync(vehicleNumber);
        DriverNameEntry.Text = record?.DriverName
            ?? shipmentsForVehicle.Select(s => s.DriverName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        DriverMobileEntry.Text = record?.DriverMobile
            ?? shipmentsForVehicle.Select(s => s.DriverPhone).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
        SetSelectedTransporter(record?.TransporterName
            ?? shipmentsForVehicle.Select(s => s.TransporterName).FirstOrDefault(n => !string.IsNullOrWhiteSpace(n)));

        // PO/inward-txn rows, from the pre-registered shipment data - one row per distinct
        // (PO, inward txn) pair; several SKU rows sharing a pair collapse into a single row.
        ClearPoTxnRows();
        var pairs = shipmentsForVehicle
            .GroupBy(s => (s.PoNumber, s.InwardTransactionId))
            .Select(g => g.Key);

        foreach (var (po, txn) in pairs)
        {
            AddPoTxnRow(po, string.IsNullOrWhiteSpace(txn) ? VehicleLookupService.GenerateInwardTxnNumber() : txn);
        }

        if (_poTxnRows.Count == 0)
        {
            AddPoTxnRow(null, VehicleLookupService.GenerateInwardTxnNumber());
        }
    }

    private void SetSelectedTransporter(string? transporter)
    {
        _selectedTransporter = transporter;
        TransporterLabel.Text = string.IsNullOrWhiteSpace(transporter) ? "Select transporter" : transporter;
        TransporterLabel.TextColor = (Color)Application.Current!.Resources[
            string.IsNullOrWhiteSpace(transporter) ? "TextSecondaryLight" : "TextPrimaryLight"];
    }

    private async void OnTransporterFieldTapped(object? sender, EventArgs e)
    {
        var tcs = new TaskCompletionSource<string?>();
        await Navigation.PushModalAsync(new TransporterPickerPage(result => tcs.TrySetResult(result)));
        var selected = await tcs.Task;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SetSelectedTransporter(selected);
        }
    }

    // Cards render in a horizontal swipeable strip (matches DocumentStrip/ExitPhotoStrip's
    // existing ScrollView+HorizontalStackLayout pattern elsewhere in this app) rather than a
    // vertical list, with a same-size "add" tile always trailing the last card. The whole strip
    // is rebuilt from _poTxnRows on every add/remove - simplest way to keep the add-tile pinned
    // at the end without manual index-juggling.
    private const double PoTxnCardWidth = 230;
    private const double PoTxnCardHeight = 176;

    private void AddPoTxnRow(string? poNumber, string? txnNumber)
    {
        var poEntry = new Entry { Placeholder = "e.g. PO-1001", Text = poNumber };
        var txnEntry = new Entry { Placeholder = "e.g. TXN-0001", Text = txnNumber };
        poEntry.TextChanged += OnPoTxnEntryTextChanged;
        txnEntry.TextChanged += OnPoTxnEntryTextChanged;

        var border = new Border
        {
            Style = (Style)Application.Current!.Resources["CardBorder"],
            Padding = new Thickness(14),
            WidthRequest = PoTxnCardWidth,
            HeightRequest = PoTxnCardHeight
        };

        var row = new PoTxnRow { Container = border, PoEntry = poEntry, TxnEntry = txnEntry };

        var removeButton = new Button
        {
            Text = "Remove row",
            FontSize = 12,
            FontFamily = "PoppinsSemiBold",
            BackgroundColor = Colors.Transparent,
            TextColor = (Color)Application.Current!.Resources["StatusException"],
            HorizontalOptions = LayoutOptions.End,
            Padding = new Thickness(0)
        };
        removeButton.Clicked += (_, _) => RemovePoTxnRow(row);

        border.Content = new VerticalStackLayout
        {
            Spacing = 10,
            Children =
            {
                BuildLabeledPoTxnField("PO NUMBER", poEntry),
                BuildLabeledPoTxnField("INWARD TRANSACTION NUMBER", txnEntry),
                removeButton
            }
        };

        _poTxnRows.Add(row);
        RenderPoTxnRows();
    }

    private static View BuildLabeledPoTxnField(string label, Entry entry) => new VerticalStackLayout
    {
        Spacing = 4,
        Children =
        {
            new Label
            {
                Text = label, FontSize = 11, FontFamily = "PoppinsSemiBold",
                TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"]
            },
            entry
        }
    };

    private View BuildAddPoTxnTile()
    {
        var tile = new Border
        {
            Stroke = (Color)Application.Current!.Resources["Primary"],
            StrokeThickness = 1.5,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
            BackgroundColor = Colors.Transparent,
            WidthRequest = PoTxnCardWidth,
            HeightRequest = PoTxnCardHeight,
            Padding = 14,
            Content = new VerticalStackLayout
            {
                Spacing = 8,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = "+", FontFamily = "PoppinsBold", FontSize = 28,
                        TextColor = (Color)Application.Current!.Resources["Primary"], HorizontalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = "Add PO / Transaction", FontFamily = "PoppinsSemiBold", FontSize = 12,
                        TextColor = (Color)Application.Current!.Resources["Primary"], HorizontalOptions = LayoutOptions.Center,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
                }
            }
        };
        tile.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => AddPoTxnRow(null, VehicleLookupService.GenerateInwardTxnNumber()))
        });
        return tile;
    }

    private void RenderPoTxnRows()
    {
        PoTxnPairsContainer.Children.Clear();
        foreach (var row in _poTxnRows)
        {
            PoTxnPairsContainer.Children.Add(row.Container);
        }
        PoTxnPairsContainer.Children.Add(BuildAddPoTxnTile());
    }

    private void OnPoTxnEntryTextChanged(object? sender, TextChangedEventArgs e)
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

        if (!string.IsNullOrWhiteSpace(upper))
        {
            PoTxnErrorLabel.IsVisible = false;
            RefreshTabErrorBadges();
        }
    }

    private void RemovePoTxnRow(PoTxnRow row)
    {
        _poTxnRows.Remove(row);
        RenderPoTxnRows();
    }

    private void ClearPoTxnRows()
    {
        _poTxnRows.Clear();
        RenderPoTxnRows();
    }

    private async void OnCaptureDocumentClicked(object? sender, EventArgs e)
    {
        var localPath = await CapturePhotoToLocalCacheAsync();
        if (localPath is null)
        {
            return;
        }

        var typeIndex = DocumentTypePicker.SelectedIndex < 0 ? 0 : DocumentTypePicker.SelectedIndex;
        _pendingDocuments.Add((localPath, DocumentTypes[typeIndex].Value));
        RenderDocumentThumbnails();
    }

    private async Task OnPhotoSlotTappedAsync(string typeValue)
    {
        var localPath = await CapturePhotoToLocalCacheAsync();
        if (localPath is null)
        {
            return;
        }

        // Tapping a slot that's already filled recaptures/overwrites it - one photo per angle.
        _pendingPhotosBySlot[typeValue] = localPath;
        RenderPhotoSlotTiles();
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

    private void RenderDocumentThumbnails()
    {
        DocumentStrip.Children.Clear();
        foreach (var (localPath, type) in _pendingDocuments)
        {
            DocumentStrip.Children.Add(BuildThumbnailTile(localPath, type));
        }
    }

    private void RenderPhotoSlotTiles()
    {
        PhotoSlotsGrid.ColumnDefinitions.Clear();
        for (var c = 0; c < _photoGridColumns; c++)
        {
            PhotoSlotsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        }

        var rows = (int)Math.Ceiling(PhotoSlots.Length / (double)_photoGridColumns);
        PhotoSlotsGrid.RowDefinitions.Clear();
        for (var r = 0; r < rows; r++)
        {
            PhotoSlotsGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }

        PhotoSlotsGrid.Children.Clear();
        for (var i = 0; i < PhotoSlots.Length; i++)
        {
            var (display, value) = PhotoSlots[i];
            var tile = BuildPhotoSlotTile(display, value);
            Grid.SetRow(tile, i / _photoGridColumns);
            Grid.SetColumn(tile, i % _photoGridColumns);
            PhotoSlotsGrid.Children.Add(tile);
        }
    }

    private View BuildPhotoSlotTile(string display, string typeValue)
    {
        var hasPhoto = _pendingPhotosBySlot.TryGetValue(typeValue, out var localPath);
        var accentColor = (Color)Application.Current!.Resources["Primary"];
        var mutedColor = (Color)Application.Current!.Resources["TextSecondaryLight"];

        var content = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Center };
        content.Children.Add(hasPhoto
            ? new Image { Source = ImageSource.FromFile(localPath), Aspect = Aspect.AspectFill, WidthRequest = 60, HeightRequest = 44 }
            : new Label
            {
                Text = IconGlyphs.Camera,
                FontFamily = "FaSolid",
                FontSize = 18,
                TextColor = mutedColor,
                HorizontalOptions = LayoutOptions.Center
            });
        content.Children.Add(new Label
        {
            Text = display,
            FontSize = 10,
            FontFamily = "PoppinsSemiBold",
            TextColor = hasPhoto ? accentColor : mutedColor,
            HorizontalOptions = LayoutOptions.Center
        });

        var tile = new Border
        {
            Stroke = hasPhoto ? accentColor : (Color)Application.Current!.Resources["CardBorderLight"],
            StrokeThickness = hasPhoto ? 2 : 1,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Padding = 8,
            HeightRequest = 84,
            Content = content
        };
        SemanticProperties.SetDescription(tile, hasPhoto ? $"{display} photo captured, tap to retake" : $"Capture {display} photo");
        tile.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await OnPhotoSlotTappedAsync(typeValue)) });
        return tile;
    }

    private static View BuildThumbnailTile(string localPath, string? caption)
    {
        var content = new VerticalStackLayout
        {
            Spacing = 2,
            Children =
            {
                new Image { Source = ImageSource.FromFile(localPath), Aspect = Aspect.AspectFill, WidthRequest = 72, HeightRequest = 56 }
            }
        };

        if (caption is not null)
        {
            content.Children.Add(new Label { Text = caption, FontSize = 9, HorizontalOptions = LayoutOptions.Center });
        }

        return new Border
        {
            WidthRequest = 72,
            Padding = 2,
            StrokeThickness = 0,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = content
        };
    }

    // One vehicle can be delivering against several POs at once - each (PO, inward txn) row
    // becomes its own InwardTransaction, created by calling the existing single check-in endpoint
    // once per row (same vehicle/driver/transporter/gate/GPS/remarks each time), with the same
    // captured documents/photos then uploaded to every transaction created. A failure on one row
    // (e.g. a PO that doesn't exist) doesn't block the others - each is independent.
    private async void OnSubmitClicked(object? sender, EventArgs e)
    {
        if (_selectedTab < 2)
        {
            SelectTab(_selectedTab + 1);
            return;
        }

        var vehicleValid = _selectedVehicleNumber is not null;
        VehicleNumberBorder.Stroke = vehicleValid ? Colors.Transparent : (Color)Application.Current!.Resources["StatusException"];
        VehicleNumberErrorLabel.IsVisible = !vehicleValid;

        var rowsValid = _poTxnRows.Count > 0
            && _poTxnRows.All(r => !string.IsNullOrWhiteSpace(r.PoEntry.Text) && !string.IsNullOrWhiteSpace(r.TxnEntry.Text));
        PoTxnErrorLabel.IsVisible = !rowsValid;

        RefreshTabErrorBadges();

        if (!vehicleValid || !rowsValid)
        {
            // Jump to whichever tab holds the first invalid field so the highlighted border is
            // actually visible - the other tabs are hidden (IsVisible=False) while not selected.
            SelectTab(!vehicleValid ? 0 : 1);
            return;
        }

        var vehicleNumber = _selectedVehicleNumber!;
        var pairs = _poTxnRows.Select(r => (PoNumber: r.PoEntry.Text!.Trim(), TxnNumber: r.TxnEntry.Text!.Trim())).ToList();

        SubmitButton.IsEnabled = false;
        SubmitButton.Text = "Checking in…";
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;

        var createdJobs = new List<InwardJob>();
        var failures = new List<(string PoNumber, string Reason)>();
        var uploadFailures = 0;

        try
        {
            var location = await TryGetLocationAsync();
            var driverName = string.IsNullOrWhiteSpace(DriverNameEntry.Text) ? null : DriverNameEntry.Text.Trim();
            var driverMobile = string.IsNullOrWhiteSpace(DriverMobileEntry.Text) ? null : DriverMobileEntry.Text.Trim();
            var transporterName = string.IsNullOrWhiteSpace(_selectedTransporter) ? null : _selectedTransporter;
            var gateName = GatePicker.SelectedItem as string;
            var remarks = string.IsNullOrWhiteSpace(RemarksEditor.Text) ? null : RemarksEditor.Text.Trim();

            foreach (var (poNumber, inwardTxn) in pairs)
            {
                try
                {
                    var job = await ApiClient.GateCheckInAsync(new GateCheckInInput
                    {
                        VehicleNumber = vehicleNumber,
                        InwardTxnNumber = inwardTxn,
                        PONumber = poNumber,
                        DriverName = driverName,
                        DriverMobile = driverMobile,
                        TransporterName = transporterName,
                        GateName = gateName,
                        GpsLatitude = location?.Latitude,
                        GpsLongitude = location?.Longitude,
                        Remarks = remarks
                    });

                    // The vehicle is already gated in by this point for this pair - an upload
                    // failing from here on shouldn't read as "nothing happened". Each upload gets
                    // its own try/catch so one failure doesn't block the rest.
                    foreach (var (localPath, type) in _pendingDocuments)
                    {
                        try { job = await ApiClient.UploadGateDocumentAsync(job.Id, type, localPath); }
                        catch (Exception) { uploadFailures++; }
                    }
                    foreach (var (type, localPath) in _pendingPhotosBySlot)
                    {
                        try { job = await ApiClient.UploadGatePhotoAsync(job.Id, type, localPath); }
                        catch (Exception) { uploadFailures++; }
                    }

                    createdJobs.Add(job);
                }
                catch (ApiException ex)
                {
                    failures.Add((poNumber, ex.Message));
                }
                catch (Exception)
                {
                    failures.Add((poNumber, "Could not reach the server."));
                }
            }

            if (createdJobs.Count == 0)
            {
                ShowError(failures.Count == 1
                    ? failures[0].Reason
                    : "None of the transactions could be created. " + string.Join(" ", failures.Select(f => $"PO {f.PoNumber}: {f.Reason}")));
                return;
            }

            ShowSuccessResult(createdJobs, failures, uploadFailures);

            _sessionCount++;
            foreach (var job in createdJobs)
            {
                _recentCheckIns.Insert(0, new RecentCheckIn(vehicleNumber, job.PONumber, DateTime.Now));
            }
            while (_recentCheckIns.Count > 5)
            {
                _recentCheckIns.RemoveAt(_recentCheckIns.Count - 1);
            }
            RenderRecentCheckIns();

            ResetForm();
        }
        finally
        {
            SubmitButton.IsEnabled = true;
            SubmitButton.Text = _selectedTab < 2 ? "Next" : "Gate-In Vehicle";
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
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

    private void ResetForm()
    {
        _selectedVehicleNumber = null;
        VehicleNumberLabel.Text = "Select expected vehicle";
        VehicleNumberLabel.TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"];
        VehicleNumberBorder.Stroke = Colors.Transparent;
        VehicleNumberErrorLabel.IsVisible = false;

        DriverNameEntry.Text = string.Empty;
        DriverMobileEntry.Text = string.Empty;
        SetSelectedTransporter(null);
        GatePicker.SelectedIndex = -1;
        RemarksEditor.Text = string.Empty;

        ClearPoTxnRows();
        AddPoTxnRow(null, VehicleLookupService.GenerateInwardTxnNumber());
        PoTxnErrorLabel.IsVisible = false;

        _pendingDocuments.Clear();
        _pendingPhotosBySlot.Clear();
        RenderDocumentThumbnails();
        RenderPhotoSlotTiles();

        RefreshTabErrorBadges();
        SelectTab(0);
    }

    private void RenderRecentCheckIns()
    {
        RecentSection.IsVisible = _recentCheckIns.Count > 0;
        RecentCountLabel.Text = _sessionCount.ToString();
        RecentCheckInsContainer.Children.Clear();

        foreach (var entry in _recentCheckIns)
        {
            var border = new Border
            {
                BackgroundColor = Color.FromArgb("#F7FBFA"),
                Stroke = (Color)Application.Current!.Resources["CardBorderLight"],
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                Padding = new Thickness(14, 12)
            };

            var vehicleIcon = new Label
            {
                Text = IconGlyphs.Truck,
                FontFamily = "FaSolid",
                FontSize = 15,
                TextColor = (Color)Application.Current.Resources["Primary"],
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            var iconTile = new Border
            {
                WidthRequest = 36,
                HeightRequest = 36,
                BackgroundColor = (Color)Application.Current.Resources["Secondary"],
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                Content = vehicleIcon,
                VerticalOptions = LayoutOptions.Center
            };

            var vehicleLabel = new Label
            {
                Text = entry.VehicleNumber,
                FontFamily = "PoppinsSemiBold",
                FontSize = 13,
                TextColor = (Color)Application.Current.Resources["TextPrimaryLight"],
                LineBreakMode = LineBreakMode.TailTruncation
            };
            var poLabel = new Label
            {
                Text = $"PO {entry.PoNumber}",
                FontFamily = "PoppinsRegular",
                FontSize = 11,
                TextColor = (Color)Application.Current.Resources["TextSecondaryLight"],
                LineBreakMode = LineBreakMode.TailTruncation
            };
            var details = new VerticalStackLayout
            {
                Spacing = 1,
                VerticalOptions = LayoutOptions.Center,
                Children = { vehicleLabel, poLabel }
            };

            var timeLabel = new Label
            {
                Text = entry.CheckedInAt.ToString("h:mm tt"),
                FontFamily = "PoppinsSemiBold",
                FontSize = 11,
                TextColor = (Color)Application.Current.Resources["TextSecondaryLight"],
                VerticalOptions = LayoutOptions.Center
            };
            var checkIcon = new Label
            {
                Text = IconGlyphs.CircleCheck,
                FontFamily = "FaSolid",
                FontSize = 15,
                TextColor = (Color)Application.Current.Resources["StatusSuccess"],
                VerticalOptions = LayoutOptions.Center
            };

            var row = new Grid
            {
                ColumnDefinitions =
                {
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Star),
                    new ColumnDefinition(GridLength.Auto),
                    new ColumnDefinition(GridLength.Auto)
                },
                ColumnSpacing = 12
            };
            row.Add(iconTile, 0);
            row.Add(details, 1);
            row.Add(timeLabel, 2);
            row.Add(checkIcon, 3);

            border.Content = row;
            RecentCheckInsContainer.Children.Add(border);
        }
    }

    // Shown as a top-right toast rather than an inline banner - the guard's already-entered data
    // stays exactly where it was instead of the form jumping around, and a successful gate-in
    // still leaves the form for the dedicated result screen exactly as before.
    private void ShowError(string message) => _ = Toast.ShowAsync(message, ToastSeverity.Error);

    private void ShowSuccessResult(List<InwardJob> jobs, List<(string PoNumber, string Reason)> failures, int uploadFailures = 0)
    {
        SuccessDetailLabel.Text = jobs.Count == 1
            ? $"{jobs[0].VehicleNumber} · PO {jobs[0].PONumber} · {jobs[0].SupplierName}\nPushed to supervisors for assignment."
            : $"{jobs[0].VehicleNumber} · {jobs.Count} transactions created (PO {string.Join(", ", jobs.Select(j => j.PONumber))})\nPushed to supervisors for assignment.";

        SuccessBadgesContainer.Children.Clear();
        if (jobs.Any(j => j.IsNewVehicle))
        {
            SuccessBadgesContainer.Children.Add(BuildBadge("New vehicle — flagged for office review", "StatusAssigned"));
        }
        if (jobs.Any(j => j.HasDeliveryDateMismatch))
        {
            SuccessBadgesContainer.Children.Add(BuildBadge("Outside expected delivery window", "StatusException"));
        }
        if (uploadFailures > 0)
        {
            SuccessBadgesContainer.Children.Add(BuildBadge(
                uploadFailures == 1 ? "1 attachment failed to upload — retry from the job later" : $"{uploadFailures} attachments failed to upload — retry from the job later",
                "StatusException"));
        }
        foreach (var (poNumber, reason) in failures)
        {
            SuccessBadgesContainer.Children.Add(BuildBadge($"PO {poNumber} not created: {reason}", "StatusException"));
        }

        FormSection.IsVisible = false;
        SuccessSection.IsVisible = true;
    }

    private async void OnBackToHomeClicked(object? sender, EventArgs e)
    {
        FormSection.IsVisible = true;
        SuccessSection.IsVisible = false;
        await Shell.Current.GoToAsync("//SecurityTabs/SecurityDashboardPage");
    }

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
