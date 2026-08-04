using System.Globalization;
using System.Text.Json;
using WarehouseGate.Mobile.Converters;
using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

[QueryProperty(nameof(JobId), "id")]
public partial class OutwardJobDetailPage : ContentPage
{
    private static readonly string[] StepLabels = { "Pick", "Assign", "Dock", "Load", "Done" };
    private static readonly StatusToColorConverter ColorConverter = new();
    private static readonly StatusToDisplayTextConverter TextConverter = new();

    private int _jobId;
    private OutwardJob? _job;
    private readonly List<LoadLineRow> _loadLineRows = new();
    private readonly Dictionary<int, string> _localPhotoPaths = new();
    private string? _selectedExceptionReason;
    private bool _pickerCoordinated;
    private bool _materialsVerified;
    private bool _materialsStaged;
    private string? _lastVizPayload;
    private bool _vizResendBurstStarted;
    private int _vizResendCount;
    private Dictionary<int, int> _confirmedQtyByLineId = new();
    private List<VehicleOption> _dockVehicleOptions = new();
    private string? _selectedDockVehicleNumber;
    private bool _baysFetched;
    private string? _selectedBayName;
    private bool? _isWideLayout;

    private class LoadLineRow
    {
        public required DispatchOrderLine Line { get; init; }
        public required Entry QtyEntry { get; init; }
        public required Entry NotesEntry { get; init; }
        public required View Card { get; init; }
        public required bool ReadOnly { get; init; }
        public required BoxView Swatch { get; init; }
    }

    public string JobId
    {
        set
        {
            if (int.TryParse(value, out var id))
            {
                _jobId = id;
            }
        }
    }

    public OutwardJobDetailPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SupervisorHubClient.OutwardJobUpdated += OnHubOutwardJobUpdated;
        Shell.SetFlyoutBehavior(this, Session.IsSecurity || Session.IsSupervisor ? FlyoutBehavior.Locked : FlyoutBehavior.Disabled);
        SupervisorNavBar.IsVisible = false;
        _ = LoadAsync();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        var wide = ResponsiveHelper.IsWide(width);
        if (_isWideLayout == wide)
        {
            return;
        }

        _isWideLayout = wide;
        ApplyResponsiveLayout(wide);
    }

    private void ApplyResponsiveLayout(bool wide)
    {
        PageContent.Padding = wide ? new Thickness(30, 24, 30, 32) : new Thickness(16, 18, 16, 24);

        ConfigureHeroLayout(wide);
        ConfigureSummaryGrid(HeroSummaryGrid, wide);
        ConfigureTabletDashboardLayout(wide);
        ConfigureDockInLayout(wide);
        ConfigureSectionHeader(StartLoadingHeaderGrid, wide);
        ConfigureChecklistLayout(wide);
        ConfigureTwoColumnAction(StartLoadingFooterGrid, StartLoadingButton, wide, 220);
        ConfigureSectionHeader(PhotoHeaderGrid, wide);
        ConfigureSectionHeader(LoadVizHeaderGrid, wide);
        ConfigureLoadVizLayout(wide);
        ConfigureTwoColumnAction(LoadLinesHeaderGrid, SubmitLoadLinesButton, wide, 170);
        ConfigureExceptionOptions(wide);
    }

    private void ConfigureHeroLayout(bool wide)
    {
        if (wide)
        {
            HeroGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star),
                new(new GridLength(1)),
                new(GridLength.Auto)
            };
            HeroGrid.RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Auto)
            };
            Grid.SetRow(HeroTextStack, 0);
            Grid.SetColumn(HeroTextStack, 1);
            Grid.SetColumnSpan(HeroTextStack, 1);
            Grid.SetRow(HeroMetaStack, 0);
            Grid.SetColumn(HeroMetaStack, 3);
            Grid.SetColumnSpan(HeroMetaStack, 1);
            HeroMetaStack.HorizontalOptions = LayoutOptions.End;
            HeroDivider.IsVisible = true;
            return;
        }

        HeroGrid.ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Star)
        };
        HeroGrid.RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Auto)
        };
        Grid.SetRow(HeroTextStack, 0);
        Grid.SetColumn(HeroTextStack, 1);
        Grid.SetColumnSpan(HeroTextStack, 1);
        Grid.SetRow(HeroMetaStack, 1);
        Grid.SetColumn(HeroMetaStack, 0);
        Grid.SetColumnSpan(HeroMetaStack, 2);
        HeroMetaStack.HorizontalOptions = LayoutOptions.Start;
        HeroDivider.IsVisible = false;
    }

    private static void ConfigureSummaryGrid(Grid grid, bool wide)
    {
        if (wide)
        {
            grid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star)
            };
            grid.RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto) };
            for (var i = 0; i < grid.Children.Count; i++)
            {
                SetChildPosition(grid, i, 0, i);
            }
            return;
        }

        grid.ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star) };
        grid.RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto)
        };
        for (var i = 0; i < grid.Children.Count; i++)
        {
            SetChildPosition(grid, i, i, 0);
        }
    }

    private void ConfigureTabletDashboardLayout(bool wide)
    {
        if (wide)
        {
            TabletDashboardGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Star)
            };
            TabletDashboardGrid.RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto)
            };
            SetChildPosition(TabletDashboardGrid, 0, 0, 0);
            // Load visualization gets its own full-width row instead of sharing a column with
            // Photo/Exception - matches the full-row 3D view used on the dedicated Plan & Load
            // editor page, rather than being squeezed into half the screen here.
            SetChildPosition(TabletDashboardGrid, 1, 1, 0, columnSpan: 2);
            Grid.SetRowSpan((BindableObject)TabletDashboardGrid.Children[1], 1);
            SetChildPosition(TabletDashboardGrid, 2, 0, 1);
            SetChildPosition(TabletDashboardGrid, 3, 2, 0);
            SetChildPosition(TabletDashboardGrid, 4, 3, 1);
            SetChildPosition(TabletDashboardGrid, 5, 3, 0);
            SetChildPosition(TabletDashboardGrid, 6, 2, 1);
            return;
        }

        TabletDashboardGrid.ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star) };
        TabletDashboardGrid.RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto)
        };

        for (var i = 0; i < TabletDashboardGrid.Children.Count; i++)
        {
            SetChildPosition(TabletDashboardGrid, i, i, 0);
            Grid.SetRowSpan((BindableObject)TabletDashboardGrid.Children[i], 1);
        }
    }

    private void ConfigureDockInLayout(bool wide)
    {
        ConfigureSectionHeader(DockInHeaderGrid, wide);
        if (wide)
        {
            DockInGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(new GridLength(1.15, GridUnitType.Star)),
                new(new GridLength(0.85, GridUnitType.Star))
            };
            DockInGrid.RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto)
            };
            SetChildPosition(DockInGrid, 0, 0, 0, 2);
            SetChildPosition(DockInGrid, 1, 1, 0);
            SetChildPosition(DockInGrid, 2, 1, 1);
            SetChildPosition(DockInGrid, 3, 2, 0, 2);
            ConfigureTwoColumnAction(DockInFooterGrid, DockInButton, true, 220);
            return;
        }

        DockInGrid.ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star) };
        DockInGrid.RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto)
        };
        SetChildPosition(DockInGrid, 0, 0, 0);
        SetChildPosition(DockInGrid, 1, 1, 0);
        SetChildPosition(DockInGrid, 2, 2, 0);
        SetChildPosition(DockInGrid, 3, 3, 0);
        ConfigureTwoColumnAction(DockInFooterGrid, DockInButton, false, 220);
    }

    private static void ConfigureSectionHeader(Grid grid, bool wide)
    {
        if (wide)
        {
            grid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star),
                new(GridLength.Auto)
            };
            grid.RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto) };
            for (var i = 0; i < grid.Children.Count; i++)
            {
                SetChildPosition(grid, i, 0, i);
            }
            return;
        }

        grid.ColumnDefinitions = new ColumnDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Star)
        };
        grid.RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Auto)
        };
        for (var i = 0; i < grid.Children.Count; i++)
        {
            if (i < 2)
            {
                SetChildPosition(grid, i, 0, i);
            }
            else
            {
                SetChildPosition(grid, i, 1, 0, 2);
                if (grid.Children[i] is View trailing)
                {
                    trailing.HorizontalOptions = LayoutOptions.Start;
                }
            }
        }
    }

    private void ConfigureChecklistLayout(bool wide)
    {
        if (wide)
        {
            ChecklistGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Star),
                new(GridLength.Star)
            };
            ChecklistGrid.RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto) };
            SetChildPosition(ChecklistGrid, 0, 0, 0);
            SetChildPosition(ChecklistGrid, 1, 0, 1);
            SetChildPosition(ChecklistGrid, 2, 0, 2);
            return;
        }

        ChecklistGrid.ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star) };
        ChecklistGrid.RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto)
        };
        SetChildPosition(ChecklistGrid, 0, 0, 0);
        SetChildPosition(ChecklistGrid, 1, 1, 0);
        SetChildPosition(ChecklistGrid, 2, 2, 0);
    }

    private static void ConfigureTwoColumnAction(Grid grid, Button button, bool wide, double buttonWidth)
    {
        if (wide)
        {
            grid.ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) };
            grid.RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto) };
            Grid.SetRow(button, 0);
            Grid.SetColumn(button, 1);
            button.WidthRequest = buttonWidth;
            return;
        }

        grid.ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star) };
        grid.RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto), new(GridLength.Auto) };
        Grid.SetRow(button, 1);
        Grid.SetColumn(button, 0);
        button.WidthRequest = -1;
    }

    private void ConfigureLoadVizLayout(bool wide)
    {
        if (wide)
        {
            LoadVizContentGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(new GridLength(2, GridUnitType.Star)),
                new(GridLength.Star)
            };
            LoadVizContentGrid.RowDefinitions = new RowDefinitionCollection { new(GridLength.Auto) };
            SetChildPosition(LoadVizContentGrid, 0, 0, 0);
            SetChildPosition(LoadVizContentGrid, 1, 0, 1);
            return;
        }

        LoadVizContentGrid.ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star) };
        LoadVizContentGrid.RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Auto)
        };
        SetChildPosition(LoadVizContentGrid, 0, 0, 0);
        SetChildPosition(LoadVizContentGrid, 1, 1, 0);
    }

    private void ConfigureExceptionOptions(bool wide)
    {
        if (wide)
        {
            ExceptionOptionsGrid.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Star),
                new(GridLength.Star)
            };
            ExceptionOptionsGrid.RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Auto),
                new(GridLength.Auto)
            };
            SetChildPosition(ExceptionOptionsGrid, 0, 0, 0);
            SetChildPosition(ExceptionOptionsGrid, 1, 0, 1);
            SetChildPosition(ExceptionOptionsGrid, 2, 1, 0);
            SetChildPosition(ExceptionOptionsGrid, 3, 1, 1);
            SetChildPosition(ExceptionOptionsGrid, 4, 2, 0, 2);
            return;
        }

        ExceptionOptionsGrid.ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star) };
        ExceptionOptionsGrid.RowDefinitions = new RowDefinitionCollection
        {
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto),
            new(GridLength.Auto)
        };
        for (var i = 0; i < ExceptionOptionsGrid.Children.Count; i++)
        {
            SetChildPosition(ExceptionOptionsGrid, i, i, 0);
        }
    }

    private static void SetChildPosition(Grid grid, int childIndex, int row, int column, int columnSpan = 1)
    {
        if (grid.Children.Count <= childIndex)
        {
            return;
        }

        var child = (BindableObject)grid.Children[childIndex];
        Grid.SetRow(child, row);
        Grid.SetColumn(child, column);
        Grid.SetColumnSpan(child, columnSpan);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SupervisorHubClient.OutwardJobUpdated -= OnHubOutwardJobUpdated;
    }

    private void OnHubOutwardJobUpdated(OutwardJob job)
    {
        if (job.Id != _jobId)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _job = job;
            RenderJob();
        });
    }

    private async Task LoadAsync()
    {
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.GetOutwardJobAsync(_jobId);

            // Supervisors working a Docked/Loading job belong in the 3D "Plan & Load" simulation,
            // not on this detail screen - regardless of how they got here (home-page tap, an
            // assignment push, dock-in on this page, or a back-navigation). "../" REPLACES this
            // page in the Shell stack so the editor's own back button goes to the previous page
            // (home), never bouncing back here into a redirect loop. Security keeps seeing this
            // page (they can't use the editor), and Completed/PendingOfficeVerification jobs still
            // open here for review (read-only - see the readOnly flag in RenderJob below).
            if (Session.IsSupervisor && _job.Status is "Docked" or "Loading")
            {
                await Shell.Current.GoToAsync($"../{nameof(LoadPlanEditorPage)}?id={_jobId}");
                return;
            }

            await LoadConfirmedQuantitiesAsync();
            RenderJob();

            // Load the dispatch review viz up front instead of leaving it on the "Tap Refresh"
            // placeholder until the user manually clicks it - this only runs once per page open
            // (OnHubOutwardJobUpdated's live-push path calls RenderJob directly, not LoadAsync,
            // so it won't re-trigger this on every realtime update).
            if (_job.Status is "Loading" or "Completed" or "PendingOfficeVerification")
            {
                await CalculateLoadingPlanAsync();
            }
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        catch (Exception)
        {
            await DisplayAlert("Error", "Could not reach the server.", "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    // Jobs that never touch the 3D "Plan & Load" flow have no options at all -
    // this fails silently (empty dict) in that case, same outcome as vehicle
    // capacity not being on file, so this never blocks the legacy flat flow.
    private async Task LoadConfirmedQuantitiesAsync()
    {
        try
        {
            var options = await ApiClient.GetLoadPlanOptionsAsync(_jobId);
            var selected = options.FirstOrDefault(o => o.IsSelected);
            if (selected is null)
            {
                _confirmedQtyByLineId = new();
                return;
            }

            var groups = await ApiClient.GetLoadPlanGroupsAsync(_jobId, selected.Id);
            _confirmedQtyByLineId = groups
                .Where(g => g.ActualQuantity.HasValue)
                .GroupBy(g => g.DispatchOrderLineId)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.ActualQuantity!.Value));
        }
        catch (ApiException)
        {
            _confirmedQtyByLineId = new();
        }
    }

    private static int StatusIndex(string status) => status switch
    {
        "PickListGenerated" => 0,
        "Assigned" => 1,
        "Docked" => 2,
        "Loading" => 3,
        "Completed" => 4,
        _ => 0
    };

    private void RenderJob()
    {
        var job = _job!;
        HeaderLabel.Text = job.DispatchOrderNumber;
        CustomerHeroLabel.Text = job.CustomerName;
        // Gate-in (Security) can set the vehicle before Dock-In (Supervisor) happens, so
        // "docked" is only true once a bay is actually assigned - not just once a vehicle exists.
        SubHeaderLabel.Text = job.BayName is null
            ? (job.VehicleNumber is null ? "No vehicle docked yet" : $"Vehicle {job.VehicleNumber} - gated in, not yet docked")
            : $"Vehicle {job.VehicleNumber} - Bay {job.BayName}";
        DockSummaryLabel.Text = string.IsNullOrWhiteSpace(job.BayName) ? "Dock pending" : job.BayName;
        OutwardLineSummaryLabel.Text = job.Lines.Count == 1 ? "1 line" : $"{job.Lines.Count} lines";

        var statusColor = (Color)ColorConverter.Convert(job.Status, typeof(Color), null, CultureInfo.CurrentCulture);
        StatusBadgeBorder.BackgroundColor = statusColor;
        StatusLabel.Text = (string)TextConverter.Convert(job.Status, typeof(string), null, CultureInfo.CurrentCulture);

        DockInSection.IsVisible = job.Status == "Assigned";
        // Already gate-checked-in by Security - pre-select rather than make them search again, but still changeable.
        if (DockInSection.IsVisible && _selectedDockVehicleNumber is null && job.VehicleNumber is not null)
        {
            SetSelectedDockVehicle(job.VehicleNumber);
        }
        if (DockInSection.IsVisible)
        {
            _ = LoadDockVehicleOptionsAsync();
        }
        if (DockInSection.IsVisible && !_baysFetched)
        {
            _baysFetched = true;
            _ = LoadBayChipsAsync();
        }
        StartLoadingSection.IsVisible = job.Status == "Docked" && !Session.IsSecurity;
        UpdateStartLoadingButtonState();

        TimeTrackingLabel.IsVisible = job.HasTimeTrackingCaption;
        TimeTrackingLabel.Text = job.TimeTrackingCaption;
        CompletionCaptionLabel.Text = job.Status == "Completed" ? "Completed on" : "Status updated";
        CompletionTimeLabel.Text = job.Status == "Completed" && job.DockOutTime is not null
            ? job.DockOutTime.Value.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt", CultureInfo.InvariantCulture)
            : StatusLabel.Text;

        var showPhotosAndLoadLines = job.Status is "Loading" or "Completed" or "PendingOfficeVerification";
        PhotoSection.IsVisible = showPhotosAndLoadLines;

        var readOnly = job.Status is "Completed" or "PendingOfficeVerification";
        var photoLimitReached = job.Photos.Count >= MaxPhotosPerJob;
        PhotoCountLabel.Text = $"{job.Photos.Count} / {MaxPhotosPerJob} photos";
        CapturePhotoButton.IsVisible = job.Status == "Loading";
        CapturePhotoButton.IsEnabled = job.Status == "Loading" && !photoLimitReached;
        PhotoLimitLabel.IsVisible = job.Status == "Loading" && photoLimitReached;

        RenderPhotos(job);
        BuildLoadLineRows(job, readOnly);

        SubmitLoadLinesButton.IsVisible = !readOnly;
        // Unlike SubmitLoadLinesButton, this "Refresh" button must stay visible once the job is
        // Completed too - CalculateLoadingPlanAsync branches to the read-only
        // CalculateCompletedLoadPlanAsync in that case, and it's the only thing that ever loads
        // the saved 3D layout into LoadVizWebView. Hiding it for readOnly jobs left the "Tap
        // Refresh to load dispatch review" placeholder permanently unreachable.
        SuggestSequenceButton.IsVisible = showPhotosAndLoadLines;

        LoadVisualizationSection.IsVisible = showPhotosAndLoadLines;
        LoadVizWebView.IsVisible = false;
        LoadVizUnavailableLabel.IsVisible = showPhotosAndLoadLines;
        LoadVizUnavailableLabel.Text = "Tap Refresh to load dispatch review.";

        var dispatchReady = job.DispatchReadyConfirmedAt is not null;
        ReviewStateLabel.Text = dispatchReady ? "Good" : "Pending";
        DispatchReadySection.IsVisible = job.Status == "Loading";
        ConfirmDispatchReadyButton.IsVisible = !dispatchReady;
        DispatchReadyConfirmedBanner.IsVisible = dispatchReady;
        if (dispatchReady)
        {
            DispatchReadyConfirmedLabel.Text = $"Dispatch ready — confirmed {job.DispatchReadyConfirmedAt:g}";
        }

        CompleteButton.IsVisible = job.Status == "Loading";
        CompleteButton.IsEnabled = job.Photos.Count > 0 && dispatchReady;
        CompleteHintLabel.IsVisible = job.Status == "Loading" && (job.Photos.Count == 0 || !dispatchReady) && !Session.IsSecurity;
        CompleteHintLabel.Text = !dispatchReady
            ? "Confirm dispatch readiness before completing."
            : "Add at least one photo before completing.";

        RestartLoadingButton.IsVisible = job.Status == "Completed" && job.GateOutTime is null && !Session.IsSecurity;

        ExceptionSection.IsVisible = showPhotosAndLoadLines;
        ExceptionInputGroup.IsVisible = showPhotosAndLoadLines && !readOnly;
        var hasException = job.ExceptionReason is not null;
        ExceptionSummaryLabel.Text = hasException ? FriendlyExceptionReason(job.ExceptionReason) : "No Exceptions";
        ExceptionStateLabel.Text = hasException ? "Review" : "Good";
        ExceptionStateLabel.TextColor = hasException
            ? (Color)Application.Current!.Resources["StatusException"]
            : (Color)Application.Current!.Resources["StatusSuccess"];
        ExceptionReportedBanner.IsVisible = hasException;
        NoExceptionBanner.IsVisible = readOnly && !hasException;
        if (hasException)
        {
            _selectedExceptionReason = job.ExceptionReason;
            RestyleExceptionOptions();
            var remarksSuffix = string.IsNullOrWhiteSpace(job.ExceptionRemarks) ? "" : $"\n{job.ExceptionRemarks}";
            ExceptionReportedLabel.Text =
                $"{FriendlyExceptionReason(job.ExceptionReason)} — reported {job.ExceptionReportedAt:g}{remarksSuffix}";
        }

        if (job.DispatchNote is not null)
        {
            DispatchNoteBorder.IsVisible = true;
            var partial = job.DispatchNote.IsPartial;
            var accent = partial
                ? (Color)Application.Current!.Resources["StatusException"]
                : (Color)Application.Current!.Resources["StatusSuccess"];
            DispatchNoteIconBadge.BackgroundColor = accent;
            DispatchNoteIconLabel.TextColor = accent;
            DispatchNoteIconLabel.Text = partial ? IconGlyphs.TriangleExclamation : IconGlyphs.ClipboardCheck;
            DispatchNoteTitleLabel.TextColor = accent;
            DispatchNoteTitleLabel.Text = partial ? "Dispatch Note — partial load" : "Dispatch Note";
            DispatchNoteLabel.Text = partial
                ? $"{job.DispatchNote.DispatchNoteNumber}, generated {job.DispatchNote.GeneratedAt.ToLocalTime():g} — stock transfer-out note required"
                : $"{job.DispatchNote.DispatchNoteNumber}, generated {job.DispatchNote.GeneratedAt.ToLocalTime():g}";
        }
    }

    private void RenderPhotos(OutwardJob job)
    {
        var photos = BuildPhotoDisplayItems(job);
        NoPhotosLabel.IsVisible = photos.Count == 0;
        PhotoScrollView.IsVisible = photos.Count > 0;
        PhotoStrip.Children.Clear();
        foreach (var photo in photos)
        {
            PhotoStrip.Children.Add(BuildPhotoTile(photo));
        }
        _ = DownloadMissingPhotosAsync(job);
    }

    private Border BuildPhotoTile(PhotoDisplayItem item)
    {
        var tile = new Border
        {
            BindingContext = item,
            Stroke = (Color)Application.Current!.Resources["CardBorderLight"],
            StrokeThickness = 1,
            BackgroundColor = (Color)Application.Current.Resources["CardLight"],
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 18 },
            WidthRequest = 220,
            HeightRequest = 150,
            Padding = 0,
            Content = new Grid
            {
                RowDefinitions = new RowDefinitionCollection
                {
                    new(new GridLength(100)),
                    new(GridLength.Auto)
                },
                Children =
                {
                    BuildPhotoPreview(item),
                    BuildPhotoCaption(item)
                }
            }
        };

        tile.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await Navigation.PushModalAsync(new PhotoViewerPage(item))) });
        return tile;
    }

    private Grid BuildPhotoPreview(PhotoDisplayItem item)
    {
        var preview = new Grid
        {
            BackgroundColor = (Color)Application.Current!.Resources["SurfaceLight"]
        };
        Grid.SetRow(preview, 0);

        if (item.HasLocalImage)
        {
            preview.Children.Add(new Image
            {
                Source = item.Source,
                Aspect = Aspect.AspectFill
            });
            return preview;
        }

        preview.Children.Add(new VerticalStackLayout
        {
            Spacing = 6,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = IconGlyphs.Camera,
                    FontFamily = "FaSolid",
                    FontSize = 22,
                    TextColor = (Color)Application.Current.Resources["Primary"],
                    HorizontalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = item.FriendlyType,
                    FontSize = 12,
                    HorizontalOptions = LayoutOptions.Center
                }
            }
        });
        return preview;
    }

    private Grid BuildPhotoCaption(PhotoDisplayItem item)
    {
        var caption = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star)
            },
            ColumnSpacing = 8,
            Padding = new Thickness(10, 9),
            Children =
            {
                new BoxView
                {
                    WidthRequest = 7,
                    HeightRequest = 7,
                    CornerRadius = 4,
                    Color = (Color)Application.Current!.Resources["Primary"],
                    VerticalOptions = LayoutOptions.Center
                },
                new Label
                {
                    Text = item.Description,
                    FontSize = 10,
                    TextColor = (Color)Application.Current.Resources["TextPrimaryLight"],
                    LineBreakMode = LineBreakMode.TailTruncation,
                    VerticalOptions = LayoutOptions.Center
                }
            }
        };
        Grid.SetRow(caption, 1);
        Grid.SetColumn((BindableObject)caption.Children[1], 1);
        return caption;
    }

    private List<PhotoDisplayItem> BuildPhotoDisplayItems(OutwardJob job) =>
        job.Photos.Select(photo => new PhotoDisplayItem
        {
            Id = photo.Id,
            Type = photo.Type,
            CapturedAt = photo.CapturedAt,
            LocalPath = _localPhotoPaths.TryGetValue(photo.Id, out var localPath) ? localPath : null
        }).ToList();

    // Anything not captured in this app session (a past session's own photo, or one captured by
    // a different device/user) has no local path yet - fetch it once, cache it, and treat it
    // exactly like a local capture from then on. Placeholder-first render above isn't blocked on
    // this; the carousel just refreshes in place once a download lands.
    private async Task DownloadMissingPhotosAsync(OutwardJob job)
    {
        var missing = job.Photos.Where(p => !_localPhotoPaths.ContainsKey(p.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var anyDownloaded = false;
        foreach (var photo in missing)
        {
            var cachedPath = Path.Combine(FileSystem.CacheDirectory, $"outward-photo-{photo.Id}{Path.GetExtension(photo.FilePath)}");
            if (File.Exists(cachedPath))
            {
                _localPhotoPaths[photo.Id] = cachedPath;
                anyDownloaded = true;
                continue;
            }

            try
            {
                var bytes = await ApiClient.DownloadFileAsync(photo.FilePath);
                await File.WriteAllBytesAsync(cachedPath, bytes);
                _localPhotoPaths[photo.Id] = cachedPath;
                anyDownloaded = true;
            }
            catch
            {
                // Leave it as a placeholder - server copy genuinely unavailable right now.
            }
        }

        if (anyDownloaded && ReferenceEquals(_job, job))
        {
            var photos = BuildPhotoDisplayItems(job);
            NoPhotosLabel.IsVisible = photos.Count == 0;
            PhotoScrollView.IsVisible = photos.Count > 0;
            PhotoStrip.Children.Clear();
            foreach (var photo in photos)
            {
                PhotoStrip.Children.Add(BuildPhotoTile(photo));
            }
        }
    }

    private async void OnPhotoTapped(object? sender, EventArgs e)
    {
        if (sender is not Border { BindingContext: PhotoDisplayItem item })
        {
            return;
        }

        await Navigation.PushModalAsync(new PhotoViewerPage(item));
    }

    // Compact one-row-per-item layout (color swatch + name + qty stepper, notes below) - the
    // physical loading order is no longer edited here (no arrows); it's entirely driven by
    // "Calculate Loading Plan" against the real rule engine, applied via ApplyLoadSequenceToRows.
    private void BuildLoadLineRows(OutwardJob job, bool readOnly)
    {
        LoadLinesContainer.Children.Clear();
        _loadLineRows.Clear();

        // Lines with a saved sequence keep their order; never-loaded lines fall to the end in
        // their original dispatch-order position.
        var orderedLines = job.Lines
            .Select((line, index) => (line, existing: job.LoadLines.FirstOrDefault(l => l.DispatchOrderLineId == line.Id), index))
            .OrderBy(x => x.existing?.LoadSequence ?? int.MaxValue)
            .ThenBy(x => x.index)
            .ToList();

        foreach (var (line, existing, index) in orderedLines)
        {
            var swatch = new BoxView
            {
                WidthRequest = 5,
                HeightRequest = 56,
                CornerRadius = 3,
                Color = (Color)Application.Current!.Resources["CardBorderLight"],
                VerticalOptions = LayoutOptions.Fill
            };

            var nameLabel = new Label
            {
                Text = line.ProductName,
                FontFamily = "PoppinsSemiBold",
                FontSize = 15,
                VerticalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.WordWrap
            };
            var orderLabel = new Label
            {
                Text = $"Ordered {line.OrderedQty} {line.UnitOfMeasure}",
                Style = (Style)Application.Current.Resources["MetaLabel"],
                FontSize = 11
            };

            // Precedence: an already-saved manual entry always wins (never silently
            // overwrite a supervisor's own prior input); otherwise pre-fill from the
            // 3D flow's confirmed quantities as a convenience default, if any exist.
            var confirmedQty = _confirmedQtyByLineId.TryGetValue(line.Id, out var cq) ? cq : (int?)null;
            var loadedQtyText = existing is not null ? existing.LoadedQty.ToString(CultureInfo.InvariantCulture) : confirmedQty?.ToString() ?? string.Empty;
            var qtyEntry = new Entry
            {
                Placeholder = "Qty",
                Keyboard = Keyboard.Numeric,
                Text = loadedQtyText,
                IsEnabled = !readOnly,
                WidthRequest = 64,
                HorizontalTextAlignment = TextAlignment.End
            };

            var minusButton = new Button { Text = IconGlyphs.Minus, FontFamily = "FaSolid", FontSize = 12, WidthRequest = 32, HeightRequest = 32, IsEnabled = !readOnly, Style = (Style)Application.Current!.Resources["ChipButton"] };
            var plusButton = new Button { Text = "+", FontFamily = "FaSolid", FontSize = 12, WidthRequest = 32, HeightRequest = 32, IsEnabled = !readOnly, Style = (Style)Application.Current!.Resources["ChipButton"] };
            minusButton.Clicked += (_, _) => AdjustQty(qtyEntry, -1);
            plusButton.Clicked += (_, _) => AdjustQty(qtyEntry, 1);

            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Auto), new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
                ColumnSpacing = 12
            };
            Grid.SetColumn(swatch, 0);
            var sequenceBadge = new Border
            {
                WidthRequest = 42,
                HeightRequest = 42,
                StrokeThickness = 0,
                BackgroundColor = (Color)Application.Current.Resources["SurfaceLight"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                VerticalOptions = LayoutOptions.Center,
                Content = new Label
                {
                    Text = (index + 1).ToString(CultureInfo.InvariantCulture),
                    FontFamily = "PoppinsBold",
                    FontSize = 13,
                    TextColor = (Color)Application.Current.Resources["Primary"],
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };
            var titleStack = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children = { nameLabel, orderLabel }
            };
            Grid.SetColumn(sequenceBadge, 1);
            Grid.SetColumn(titleStack, 2);
            row.Children.Add(swatch);
            row.Children.Add(sequenceBadge);
            row.Children.Add(titleStack);

            View qtyControl;
            if (readOnly)
            {
                qtyControl = new Border
                {
                    Padding = new Thickness(12, 8),
                    StrokeThickness = 0,
                    BackgroundColor = (Color)Application.Current.Resources["SurfaceLight"],
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 13 },
                    VerticalOptions = LayoutOptions.Center,
                    Content = new Label
                    {
                        Text = string.IsNullOrWhiteSpace(loadedQtyText) ? "Not recorded" : $"{loadedQtyText} {line.UnitOfMeasure}",
                        FontFamily = "PoppinsBold",
                        FontSize = 12,
                        TextColor = (Color)Application.Current.Resources["Primary"]
                    }
                };
            }
            else
            {
                qtyControl = new HorizontalStackLayout
                {
                    Spacing = 8,
                    VerticalOptions = LayoutOptions.Center,
                    Children = { minusButton, qtyEntry, plusButton }
                };
            }
            Grid.SetColumn(qtyControl, 3);
            row.Children.Add(qtyControl);

            var notesEntry = new Entry
            {
                Placeholder = "Notes (optional)",
                Text = existing?.Notes ?? string.Empty,
                IsEnabled = !readOnly,
                FontSize = 12
            };

            var cardContent = new VerticalStackLayout { Spacing = 10, Children = { row } };
            if (readOnly)
            {
                if (!string.IsNullOrWhiteSpace(existing?.Notes))
                {
                    cardContent.Children.Add(new Label
                    {
                        Text = existing.Notes,
                        Style = (Style)Application.Current.Resources["MetaLabel"],
                        Margin = new Thickness(56, 0, 0, 0)
                    });
                }
            }
            else
            {
                cardContent.Children.Add(new Border
                {
                    Stroke = (Color)Application.Current.Resources["CardBorderLight"],
                    StrokeThickness = 1,
                    StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                    BackgroundColor = (Color)Application.Current.Resources["CardLight"],
                    Padding = new Thickness(12, 0),
                    Content = notesEntry
                });
            }

            var rowCard = new Border
            {
                Stroke = (Color)Application.Current.Resources["CardBorderLight"],
                StrokeThickness = 1,
                BackgroundColor = (Color)Application.Current.Resources["CardLight"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
                Padding = new Thickness(14),
                Content = cardContent
            };
            LoadLinesContainer.Children.Add(rowCard);

            _loadLineRows.Add(new LoadLineRow
            {
                Line = line,
                QtyEntry = qtyEntry,
                NotesEntry = notesEntry,
                Card = rowCard,
                ReadOnly = readOnly,
                Swatch = swatch
            });
        }
    }

    private async void OnCalculateLoadingPlanClicked(object? sender, EventArgs e) => await CalculateLoadingPlanAsync();

    private async void OnOpenLoadPlanEditorClicked(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync($"{nameof(LoadPlanEditorPage)}?id={_jobId}");

    // Calls the real rule engine (collision/weight/stack/support/COG/axle/fragility/hazard/
    // temperature/orientation/door-clearance/accessibility/delivery-sequence - all 13 rules)
    // against this job's actual dispatch-order lines and real vehicle capacity, then applies
    // its recommended order to the loaded-quantities list and renders the 3D view + stats from
    // the same response. Deliberately server-driven rather than recomputed per keystroke -
    // positions now reflect real rule validation, not just an instant local sort.
    private async Task CalculateLoadingPlanAsync()
    {
        SuggestSequenceButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            if (_job?.Status is "Completed" or "PendingOfficeVerification")
            {
                await CalculateCompletedLoadPlanAsync();
                return;
            }

            var plan = await ApiClient.GetOutwardLoadPlanAsync(_jobId);
            LoadVizWebView.IsVisible = true;
            LoadVizUnavailableLabel.IsVisible = false;

            ApplyLoadSequenceToRows(plan);
            RenderLoadPlanStats(plan);
            SendLoadPlanToViewer(plan);
        }
        catch (ApiException ex)
        {
            LoadVizWebView.IsVisible = false;
            LoadPlanStatsSection.IsVisible = false;
            LoadVizUnavailableLabel.IsVisible = true;
            LoadVizUnavailableLabel.Text = ex.Message;
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            SuggestSequenceButton.IsEnabled = true;
        }
    }

    private async Task CalculateCompletedLoadPlanAsync()
    {
        var options = await ApiClient.GetLoadPlanOptionsAsync(_jobId);
        var selected = options.FirstOrDefault(o => o.IsSelected) ?? options.FirstOrDefault();
        if (selected is null)
        {
            LoadVizWebView.IsVisible = false;
            LoadPlanStatsSection.IsVisible = false;
            LoadVizUnavailableLabel.IsVisible = true;
            LoadVizUnavailableLabel.Text = "Loading plan unavailable - no saved arrangement found.";
            return;
        }

        var groups = await ApiClient.GetLoadPlanGroupsAsync(_jobId, selected.Id);
        LoadPlanValidation? validation = null;
        try
        {
            validation = await ApiClient.ValidateLoadPlanOptionAsync(_jobId, selected.Id);
        }
        catch (ApiException)
        {
            // Saved group geometry is still enough to show the completed layout.
        }

        LoadVizWebView.IsVisible = true;
        LoadVizUnavailableLabel.IsVisible = false;
        ApplyLoadSequenceToRows(groups);
        RenderCompletedLoadPlanStats(groups, validation);
        SendLoadPlanGroupsToViewer(groups);
    }

    // Matches each placed line (Sku == the dispatch-order-line id) back onto its loaded-qty
    // row: reorders the rows to follow the engine's recommended sequence and colors each
    // row's swatch to match its box color in the 3D view, tying the two lists together.
    private void ApplyLoadSequenceToRows(LoadPlanResult plan)
    {
        var sequenceByLineId = plan.Items
            .GroupBy(i => i.Sku)
            .ToDictionary(g => g.Key, g => g.Min(i => i.LoadSequence));
        var colorByLineId = plan.Items
            .GroupBy(i => i.Sku)
            .ToDictionary(g => g.Key, g => g.First().Color);

        var ordered = _loadLineRows
            .OrderBy(row => sequenceByLineId.TryGetValue(row.Line.Id.ToString(), out var seq) ? seq : int.MaxValue)
            .ToList();

        _loadLineRows.Clear();
        _loadLineRows.AddRange(ordered);

        LoadLinesContainer.Children.Clear();
        foreach (var row in ordered)
        {
            if (colorByLineId.TryGetValue(row.Line.Id.ToString(), out var hex) && Color.TryParse(hex, out var color))
            {
                row.Swatch.Color = color;
            }

            LoadLinesContainer.Children.Add(row.Card);
        }
    }

    private void ApplyLoadSequenceToRows(List<LoadPlanGroup> groups)
    {
        var sequenceByLineId = groups
            .GroupBy(g => g.DispatchOrderLineId)
            .ToDictionary(g => g.Key, g => g.Min(x => x.LoadSequence));
        var colorByLineId = groups
            .GroupBy(g => g.DispatchOrderLineId)
            .ToDictionary(g => g.Key, g => g.First().Color);

        var ordered = _loadLineRows
            .OrderBy(row => sequenceByLineId.TryGetValue(row.Line.Id, out var seq) ? seq : int.MaxValue)
            .ToList();

        _loadLineRows.Clear();
        _loadLineRows.AddRange(ordered);

        LoadLinesContainer.Children.Clear();
        foreach (var row in ordered)
        {
            if (colorByLineId.TryGetValue(row.Line.Id, out var hex) && Color.TryParse(hex, out var color))
            {
                row.Swatch.Color = color;
            }

            LoadLinesContainer.Children.Add(row.Card);
        }
    }

    private void RenderLoadPlanStats(LoadPlanResult plan)
    {
        LoadPlanStatsSection.IsVisible = true;

        TotalBoxesLabel.Text = plan.Items.Count.ToString();
        TotalWeightLabel.Text = $"{plan.Items.Sum(i => i.Weight):0.#} kg";

        WeightUtilLabel.Text = $"{plan.Simulation.WeightUtilizationPct:0.#}%";
        WeightUtilBar.Progress = Math.Clamp(plan.Simulation.WeightUtilizationPct / 100, 0, 1);
        SpaceUtilLabel.Text = $"{plan.Simulation.VehicleUtilizationPct:0.#}%";
        SpaceUtilBar.Progress = Math.Clamp(plan.Simulation.VehicleUtilizationPct / 100, 0, 1);

        CenterOfGravityLabel.Text =
            $"X: {plan.Simulation.CenterOfGravityX:0.#} cm · Y: {plan.Simulation.CenterOfGravityY:0.#} cm · Z: {plan.Simulation.CenterOfGravityZ:0.#} cm";
        var (tipRiskText, tipRiskColor) = ComputeTipRisk(plan);
        TipRiskBadge.Text = tipRiskText;
        TipRiskBadge.TextColor = tipRiskColor;

        BuildLoadingSequenceList(plan);

        var ready = plan.Simulation.UnplacedCount == 0 && plan.Simulation.RuleViolationCount == 0;
        var bannerColor = (Color)Application.Current!.Resources[ready ? "StatusSuccess" : "StatusException"];
        LoadPlanBannerBorder.Stroke = bannerColor;
        LoadPlanBannerBorder.BackgroundColor = (Color)Application.Current.Resources["CardTint"];
        LoadPlanBannerLabel.TextColor = bannerColor;
        LoadPlanBannerLabel.Text = ready
            ? "Plan ready — no issues found."
            : $"{plan.Simulation.UnplacedCount} item(s) couldn't be placed, {plan.Simulation.RuleViolationCount} rule note(s) — review before loading.";
    }

    private void RenderCompletedLoadPlanStats(List<LoadPlanGroup> groups, LoadPlanValidation? validation)
    {
        LoadPlanStatsSection.IsVisible = true;

        var loadedGroups = groups.Where(g => (g.ActualQuantity ?? g.Quantity) > 0).ToList();
        var totalBoxes = loadedGroups.Sum(g => g.ActualQuantity ?? g.Quantity);
        var totalWeight = loadedGroups.Sum(g =>
        {
            var line = _job?.Lines.FirstOrDefault(l => l.Id == g.DispatchOrderLineId);
            return (double)(line?.WeightKg ?? 0) * (g.ActualQuantity ?? g.Quantity);
        });

        TotalBoxesLabel.Text = totalBoxes.ToString(CultureInfo.InvariantCulture);
        TotalWeightLabel.Text = $"{totalWeight:0.#} kg";

        var simulation = validation?.Simulation;
        WeightUtilLabel.Text = simulation is null ? "0%" : $"{simulation.WeightUtilizationPct:0.#}%";
        WeightUtilBar.Progress = simulation is null ? 0 : Math.Clamp(simulation.WeightUtilizationPct / 100, 0, 1);
        SpaceUtilLabel.Text = simulation is null ? "0%" : $"{simulation.VehicleUtilizationPct:0.#}%";
        SpaceUtilBar.Progress = simulation is null ? 0 : Math.Clamp(simulation.VehicleUtilizationPct / 100, 0, 1);

        if (simulation is null)
        {
            CenterOfGravityLabel.Text = "Not available";
            TipRiskBadge.Text = "Review";
            TipRiskBadge.TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"];
        }
        else
        {
            CenterOfGravityLabel.Text =
                $"X: {simulation.CenterOfGravityX:0.#} cm - Y: {simulation.CenterOfGravityY:0.#} cm - Z: {simulation.CenterOfGravityZ:0.#} cm";
            var (tipRiskText, tipRiskColor) = ComputeTipRisk(simulation, _job);
            TipRiskBadge.Text = tipRiskText;
            TipRiskBadge.TextColor = tipRiskColor;
        }

        BuildLoadingSequenceList(groups);

        var warningCount = validation?.Warnings.Count ?? 0;
        var unplacedCount = simulation?.UnplacedCount ?? 0;
        var ready = warningCount == 0 && unplacedCount == 0;
        var bannerColor = (Color)Application.Current!.Resources[ready ? "StatusSuccess" : "StatusException"];
        LoadPlanBannerBorder.Stroke = bannerColor;
        LoadPlanBannerBorder.BackgroundColor = (Color)Application.Current.Resources["CardTint"];
        LoadPlanBannerLabel.TextColor = bannerColor;
        LoadPlanBannerLabel.Text = ready
            ? "Completed layout loaded from saved arrangement."
            : $"{unplacedCount} item(s) couldn't be placed, {warningCount} rule note(s) - review saved layout.";
    }

    // Simple heuristic derived from how far the computed center of gravity drifts from the
    // vehicle's geometric center - not a full physics model, just a quick at-a-glance signal.
    private static (string Text, Color Color) ComputeTipRisk(LoadPlanResult plan)
    {
        var vehicle = plan.Vehicle;
        var lateral = vehicle.Width <= 0 ? 0 : Math.Abs(plan.Simulation.CenterOfGravityX - vehicle.Width / 2) / (vehicle.Width / 2);
        var longitudinal = vehicle.Length <= 0 ? 0 : Math.Abs(plan.Simulation.CenterOfGravityZ - vehicle.Length / 2) / (vehicle.Length / 2);
        var deviation = Math.Max(lateral, longitudinal);

        return deviation switch
        {
            < 0.15 => ("Low tip risk", (Color)Application.Current!.Resources["StatusSuccess"]),
            < 0.30 => ("Medium tip risk", (Color)Application.Current!.Resources["StatusAvailable"]),
            _ => ("High tip risk", (Color)Application.Current!.Resources["StatusException"])
        };
    }

    private static (string Text, Color Color) ComputeTipRisk(LoadSimulation simulation, OutwardJob? job)
    {
        var width = (double)(job?.VehicleWidthCm ?? 0);
        var length = (double)(job?.VehicleLengthCm ?? 0);
        var lateral = width <= 0 ? 0 : Math.Abs(simulation.CenterOfGravityX - width / 2) / (width / 2);
        var longitudinal = length <= 0 ? 0 : Math.Abs(simulation.CenterOfGravityZ - length / 2) / (length / 2);
        var deviation = Math.Max(lateral, longitudinal);

        return deviation switch
        {
            < 0.15 => ("Low tip risk", (Color)Application.Current!.Resources["StatusSuccess"]),
            < 0.30 => ("Medium tip risk", (Color)Application.Current!.Resources["StatusAvailable"]),
            _ => ("High tip risk", (Color)Application.Current!.Resources["StatusException"])
        };
    }

    private void BuildLoadingSequenceList(LoadPlanResult plan)
    {
        LoadingSequenceContainer.Children.Clear();

        var byLine = plan.Items
            .GroupBy(i => i.Sku)
            .Select(g => new { g.First().Description, Sequence = g.Min(i => i.LoadSequence) })
            .OrderBy(x => x.Sequence)
            .ToList();

        if (byLine.Count == 0)
        {
            return;
        }

        var minSeq = byLine[0].Sequence;
        var maxSeq = byLine[^1].Sequence;

        for (var i = 0; i < byLine.Count; i++)
        {
            var line = byLine[i];
            var caption = line.Sequence == minSeq ? "Load first (front)"
                : line.Sequence == maxSeq ? "Load last (door — unloaded first)"
                : "Middle";

            var badge = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = (Color)Application.Current!.Resources["Primary"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                WidthRequest = 28,
                HeightRequest = 28,
                Content = new Label
                {
                    Text = (i + 1).ToString(),
                    FontFamily = "PoppinsBold",
                    FontSize = 13,
                    TextColor = Colors.White,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };

            var textStack = new VerticalStackLayout
            {
                Spacing = 1,
                Children =
                {
                    new Label { Text = line.Description, FontFamily = "PoppinsSemiBold", FontSize = 13 },
                    new Label { Text = caption, FontFamily = "PoppinsRegular", FontSize = 11, TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"] }
                }
            };

            LoadingSequenceContainer.Children.Add(new HorizontalStackLayout { Spacing = 10, Children = { badge, textStack } });
        }
    }

    private void BuildLoadingSequenceList(List<LoadPlanGroup> groups)
    {
        LoadingSequenceContainer.Children.Clear();

        var byLine = groups
            .Where(g => (g.ActualQuantity ?? g.Quantity) > 0)
            .GroupBy(g => g.DispatchOrderLineId)
            .Select(g => new
            {
                Description = g.First().ProductName,
                Sequence = g.Min(x => x.LoadSequence),
                Quantity = g.Sum(x => x.ActualQuantity ?? x.Quantity)
            })
            .OrderBy(x => x.Sequence)
            .ToList();

        if (byLine.Count == 0)
        {
            LoadingSequenceContainer.Children.Add(new Label
            {
                Text = "No loaded SKU groups found in the saved arrangement.",
                Style = (Style)Application.Current!.Resources["MetaLabel"]
            });
            return;
        }

        var minSeq = byLine[0].Sequence;
        var maxSeq = byLine[^1].Sequence;

        for (var i = 0; i < byLine.Count; i++)
        {
            var line = byLine[i];
            var caption = line.Sequence == minSeq ? "Loaded first"
                : line.Sequence == maxSeq ? "Loaded last"
                : "Middle";

            var badge = new Border
            {
                StrokeThickness = 0,
                BackgroundColor = (Color)Application.Current!.Resources["Primary"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                WidthRequest = 28,
                HeightRequest = 28,
                Content = new Label
                {
                    Text = (i + 1).ToString(CultureInfo.InvariantCulture),
                    FontFamily = "PoppinsBold",
                    FontSize = 13,
                    TextColor = Colors.White,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            };

            var textStack = new VerticalStackLayout
            {
                Spacing = 1,
                Children =
                {
                    new Label { Text = line.Description, FontFamily = "PoppinsSemiBold", FontSize = 13 },
                    new Label { Text = $"{caption} - {line.Quantity} carton(s)", FontFamily = "PoppinsRegular", FontSize = 11, TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"] }
                }
            };

            LoadingSequenceContainer.Children.Add(new HorizontalStackLayout { Spacing = 10, Children = { badge, textStack } });
        }
    }

    // Renders the server-computed placements directly - no local packing needed, the engine
    // already resolved orientation/position for every box.
    private void SendLoadPlanToViewer(LoadPlanResult plan)
    {
        var unplacedNames = plan.UnplacedSkus
            .Select(sku => _job?.Lines.FirstOrDefault(l => l.Id.ToString() == sku)?.ProductName ?? sku)
            .Distinct()
            .ToList();

        var payload = new
        {
            vehicle = new { widthCm = plan.Vehicle.Width, lengthCm = plan.Vehicle.Length, heightCm = plan.Vehicle.Height },
            placed = plan.Items.Select((item, index) => new
            {
                x = item.X,
                y = item.Y,
                z = item.Z,
                w = item.Width,
                h = item.Height,
                d = item.Length,
                color = item.Color,
                lineIndex = index,
                name = item.Description
            }),
            unplacedNames
        };

        _lastVizPayload = JsonSerializer.Serialize(payload);
        LoadVizWebView.SendRawMessage(_lastVizPayload);
        StartVizResendBurstIfNeeded();
    }

    private void SendLoadPlanGroupsToViewer(List<LoadPlanGroup> groups)
    {
        var visibleGroups = groups
            .Where(g => (g.ActualQuantity ?? g.Quantity) > 0)
            .OrderBy(g => g.LoadSequence)
            .Select(g =>
            {
                var line = _job?.Lines.FirstOrDefault(l => l.Id == g.DispatchOrderLineId);
                return new
                {
                    groupId = g.Id,
                    name = g.ProductName,
                    qty = g.ActualQuantity ?? g.Quantity,
                    color = g.Color,
                    locked = true,
                    code = line?.SkuCode ?? "",
                    location = line?.DeliveryLocation ?? "",
                    x = g.PositionX,
                    y = g.PositionY,
                    z = g.PositionZ,
                    w = g.DimX,
                    h = g.DimY,
                    d = g.DimZ,
                    rows = g.Rows,
                    cols = g.Columns,
                    layers = g.Layers
                };
            })
            .ToList();

        var payload = new
        {
            vehicle = new
            {
                widthCm = (double)(_job?.VehicleWidthCm ?? 0),
                lengthCm = (double)(_job?.VehicleLengthCm ?? 0),
                heightCm = (double)(_job?.VehicleHeightCm ?? 0),
                number = _job?.VehicleNumber ?? "",
                typeLabel = _job?.VehicleMaxWeightKg is null ? "" : $"~{Math.Round(_job.VehicleMaxWeightKg.Value / 1000)} Ton Truck"
            },
            movingGroupId = (int?)null,
            unplacedNames = Array.Empty<string>(),
            viewMode = "3d",
            placementActive = false,
            preview = (object?)null,
            previewValid = true,
            previewMessage = "",
            skus = Array.Empty<object>(),
            options = Array.Empty<object>(),
            groupsList = visibleGroups
        };

        _lastVizPayload = JsonSerializer.Serialize(payload);
        LoadVizWebView.SendRawMessage(_lastVizPayload);
        StartVizResendBurstIfNeeded();
    }

    // HybridWebView has no reliable "page is ready" event to hook (HybridWebView doesn't
    // expose WebView's Navigated, and a hand-rolled JS "ready" ping turned out to be a
    // fragile single point of failure - if it's ever lost, the view is stuck blank forever
    // with nothing visibly wrong). Instead: send immediately every time, and also fire a
    // short burst of resends of the latest payload for the first few seconds after the
    // card becomes visible, so whichever attempt lands after the WebView finishes loading
    // three.min.js and wiring its listeners is guaranteed to get through - no handshake,
    // no permanent failure mode, self-healing regardless of how slow a given device is.
    private void StartVizResendBurstIfNeeded()
    {
        if (_vizResendBurstStarted)
        {
            return;
        }

        _vizResendBurstStarted = true;
        _vizResendCount = 0;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(500), () =>
        {
            _vizResendCount++;
            if (_lastVizPayload is not null && LoadVizWebView.IsVisible)
            {
                LoadVizWebView.SendRawMessage(_lastVizPayload);
            }
            return _vizResendCount < 6;
        });
    }

    private static void AdjustQty(Entry entry, int delta)
    {
        if (!decimal.TryParse(entry.Text, out var qty))
        {
            qty = 0;
        }
        qty = Math.Max(0, qty + delta);
        entry.Text = qty.ToString(CultureInfo.InvariantCulture);
    }

    private async Task LoadDockVehicleOptionsAsync()
    {
        if (_dockVehicleOptions.Count > 0)
        {
            return;
        }

        try
        {
            var masters = await ApiClient.GetVehicleMastersAsync();
            _dockVehicleOptions = masters
                .Select(m => new VehicleOption
                {
                    VehicleNumber = m.VehicleNumber,
                    Summary = new[] { m.DriverName, m.TransporterName }.Where(p => !string.IsNullOrWhiteSpace(p)).Any()
                        ? string.Join(" - ", new[] { m.DriverName, m.TransporterName }.Where(p => !string.IsNullOrWhiteSpace(p)))
                        : "Vehicle master"
                })
                .ToList();
        }
        catch
        {
            _dockVehicleOptions = new();
        }
    }

    private void SetSelectedDockVehicle(string? vehicleNumber)
    {
        _selectedDockVehicleNumber = vehicleNumber;
        VehicleNumberLabel.Text = string.IsNullOrWhiteSpace(vehicleNumber) ? "Select vehicle" : vehicleNumber;
        VehicleNumberLabel.TextColor = string.IsNullOrWhiteSpace(vehicleNumber)
            ? (Color)Application.Current!.Resources["TextSecondaryLight"]
            : (Color)Application.Current!.Resources["TextPrimaryLight"];
    }

    private async void OnDockVehicleFieldTapped(object? sender, EventArgs e)
    {
        await LoadDockVehicleOptionsAsync();
        if (_dockVehicleOptions.Count == 0)
        {
            await DisplayAlert("No vehicles on file", "Ask your Logistics Manager or Security to register this vehicle first.", "OK");
            return;
        }

        var tcs = new TaskCompletionSource<string?>();
        await Navigation.PushModalAsync(new ExpectedVehiclePickerPage(_dockVehicleOptions, result => tcs.TrySetResult(result)));
        var selected = await tcs.Task;
        if (!string.IsNullOrWhiteSpace(selected))
        {
            SetSelectedDockVehicle(selected);
        }
    }

    private void OnBayNumberTextChanged(object? sender, TextChangedEventArgs e)
    {
        var digitsOnly = new string((e.NewTextValue ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digitsOnly.Length > 0 && int.TryParse(digitsOnly, out var value) && value > 50)
        {
            digitsOnly = "50";
        }
        if (digitsOnly != e.NewTextValue)
        {
            BayNumberEntry.Text = digitsOnly;
        }
    }

    // Warehouses with a dock-bay master defined (Admin > Master Data > Dock Bays) get a chip
    // picker here; warehouses without one keep the legacy free-number 1-50 entry as a fallback.
    private async Task LoadBayChipsAsync()
    {
        try
        {
            var bays = await ApiClient.GetBaysAsync();
            if (bays.Count == 0)
            {
                return;
            }

            BayChipsLayout.Children.Clear();
            foreach (var bayName in bays)
            {
                var chip = new Button
                {
                    Text = bayName,
                    Style = (Style)Application.Current!.Resources["ChipButton"],
                    Margin = new Thickness(0, 0, 8, 8)
                };
                chip.Clicked += (sender, _) =>
                {
                    _selectedBayName = bayName;
                    RestyleBayChips();
                };
                BayChipsLayout.Children.Add(chip);
            }

            BayHelperLabel.Text = "Select a bay";
            BayNumberEntryGrid.IsVisible = false;
            BayChipsScroll.IsVisible = true;
        }
        catch (ApiException)
        {
            // Fall back to the numeric entry - it's already visible by default.
        }
    }

    private void RestyleBayChips()
    {
        var active = (Color)Application.Current!.Resources["Primary"];
        var mutedBorder = (Color)Application.Current.Resources["CardBorderLight"];
        var mutedText = (Color)Application.Current.Resources["TextPrimaryLight"];

        foreach (var child in BayChipsLayout.Children)
        {
            if (child is not Button chip)
            {
                continue;
            }

            var selected = chip.Text == _selectedBayName;
            chip.BackgroundColor = selected ? active : Colors.Transparent;
            chip.TextColor = selected ? Colors.White : mutedText;
            chip.BorderColor = selected ? active : mutedBorder;
        }
    }

    private async void OnDockInClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_selectedDockVehicleNumber))
        {
            await DisplayAlert("Missing details", "Select a vehicle before docking in.", "OK");
            return;
        }

        string bayName;
        if (BayChipsScroll.IsVisible)
        {
            if (string.IsNullOrWhiteSpace(_selectedBayName))
            {
                await DisplayAlert("Missing details", "Select a bay before docking in.", "OK");
                return;
            }
            bayName = _selectedBayName;
        }
        else
        {
            if (!int.TryParse(BayNumberEntry.Text, out var bayNumber) || bayNumber < 1 || bayNumber > 50)
            {
                await DisplayAlert("Missing details", "Enter a bay number between 1 and 50 before docking in.", "OK");
                return;
            }
            bayName = $"Bay-{bayNumber}";
        }

        DockInButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.DockInOutwardAsync(_jobId, _selectedDockVehicleNumber!, bayName);

            // Dock-in flows straight into the 3D load plan simulation - the supervisor shouldn't
            // land back on this detail page in between. Loading must still be STARTED first
            // (CompleteAsync hard-requires status Loading, and LoadingStartTime feeds the
            // productivity KPIs), so it's chained here rather than skipped. "../" replaces this
            // page in the Shell stack so the editor's back goes to the previous page, not here.
            // If starting fails for any reason, fall back to the normal detail page with its
            // manual checklist + Start Loading flow still available.
            try
            {
                _job = await ApiClient.StartLoadingAsync(_jobId);
                await Shell.Current.GoToAsync($"../{nameof(LoadPlanEditorPage)}?id={_jobId}");
                return;
            }
            catch (ApiException)
            {
                // Docked but not started - the page below still shows the Start Loading section.
            }

            RenderJob();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not dock in", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            DockInButton.IsEnabled = true;
        }
    }

    private void OnPickerCoordinatedTapped(object? sender, EventArgs e)
    {
        _pickerCoordinated = !_pickerCoordinated;
        RestyleChecklistOption(PickerCoordinatedOption, PickerCoordinatedLabel, _pickerCoordinated);
        UpdateStartLoadingButtonState();
    }

    private void OnMaterialsVerifiedTapped(object? sender, EventArgs e)
    {
        _materialsVerified = !_materialsVerified;
        RestyleChecklistOption(MaterialsVerifiedOption, MaterialsVerifiedLabel, _materialsVerified);
        UpdateStartLoadingButtonState();
    }

    private void OnMaterialsStagedTapped(object? sender, EventArgs e)
    {
        _materialsStaged = !_materialsStaged;
        RestyleChecklistOption(MaterialsStagedOption, MaterialsStagedLabel, _materialsStaged);
        UpdateStartLoadingButtonState();
    }

    // Same border-color-selected convention as the exception reasons below, but success-accented
    // since these are affirmative confirmations rather than a problem being flagged.
    private static void RestyleChecklistOption(Border border, Label label, bool selected)
    {
        var mutedBorder = (Color)Application.Current!.Resources["CardBorderLight"];
        var mutedText = (Color)Application.Current.Resources["TextSecondaryLight"];
        var activeColor = (Color)Application.Current.Resources["StatusSuccess"];
        var activeBackground = (Color)Application.Current.Resources["StatusSuccessTint"];
        var mutedBackground = (Color)Application.Current.Resources["CardLight"];

        border.Stroke = selected ? activeColor : mutedBorder;
        border.StrokeThickness = selected ? 2 : 1.5;
        border.BackgroundColor = selected ? activeBackground : mutedBackground;
        label.TextColor = selected ? activeColor : mutedText;
        label.FontFamily = selected ? "PoppinsBold" : "PoppinsSemiBold";
    }

    private void UpdateStartLoadingButtonState()
    {
        var completed = (_pickerCoordinated ? 1 : 0) + (_materialsVerified ? 1 : 0) + (_materialsStaged ? 1 : 0);
        ChecklistProgressLabel.Text = $"{completed} / 3";
        StartLoadingButton.IsEnabled = completed == 3;
    }

    private async void OnStartLoadingClicked(object? sender, EventArgs e)
    {
        StartLoadingButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.StartLoadingAsync(_jobId);
            // Same handoff as dock-in: loading has begun, so go straight into the 3D load plan
            // simulation instead of leaving the supervisor on this detail page.
            await Shell.Current.GoToAsync($"../{nameof(LoadPlanEditorPage)}?id={_jobId}");
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not start", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            StartLoadingButton.IsEnabled = true;
        }
    }

    private const int MaxPhotosPerJob = 5;

    private async void OnCapturePhotoClicked(object? sender, EventArgs e) => await CapturePhotoAsync();

    private async Task CapturePhotoAsync()
    {
        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("Not supported", "This device does not support photo capture.", "OK");
                return;
            }

            if ((_job?.Photos.Count ?? 0) >= MaxPhotosPerJob)
            {
                await DisplayAlert("Limit reached", $"You can add up to {MaxPhotosPerJob} photos per job.", "OK");
                return;
            }

            var localPath = await PhotoCapture.CaptureAndSaveAsync();
            if (localPath is null)
            {
                return;
            }

            CapturePhotoButton.IsEnabled = false;
            Spinner.IsVisible = true;
            Spinner.IsRunning = true;

            _job = await ApiClient.UploadOutwardPhotoAsync(_jobId, "VehicleLoaded", localPath);
            var newest = _job.Photos.OrderBy(p => p.Id).LastOrDefault();
            if (newest is not null)
            {
                _localPhotoPaths[newest.Id] = localPath;
            }
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Upload failed", ex.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Camera error", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            RenderJob();
        }
    }

    private async void OnSubmitLoadLinesClicked(object? sender, EventArgs e)
    {
        var lines = new List<LoadLineInput>();
        for (var i = 0; i < _loadLineRows.Count; i++)
        {
            var row = _loadLineRows[i];
            if (!decimal.TryParse(row.QtyEntry.Text, out var qty))
            {
                await DisplayAlert("Missing quantity", $"Enter loaded quantity for {row.Line.ProductName}.", "OK");
                return;
            }

            lines.Add(new LoadLineInput
            {
                DispatchOrderLineId = row.Line.Id,
                LoadedQty = qty,
                LoadSequence = i + 1,
                Notes = string.IsNullOrWhiteSpace(row.NotesEntry.Text) ? null : row.NotesEntry.Text.Trim()
            });
        }

        SubmitLoadLinesButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.SubmitLoadLinesAsync(_jobId, lines);
            RenderJob();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not save load lines", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            SubmitLoadLinesButton.IsEnabled = true;
        }
    }

    private void OnPartialLoadTapped(object? sender, EventArgs e) => SelectExceptionReason("PartialLoad");
    private void OnStockNotAvailableTapped(object? sender, EventArgs e) => SelectExceptionReason("StockNotAvailable");
    private void OnCapacityExceededTapped(object? sender, EventArgs e) => SelectExceptionReason("VehicleCapacityExceeded");
    private void OnMaterialDamagedTapped(object? sender, EventArgs e) => SelectExceptionReason("MaterialDamaged");
    private void OnWrongMaterialTapped(object? sender, EventArgs e) => SelectExceptionReason("WrongMaterialLoaded");

    private void SelectExceptionReason(string reason)
    {
        _selectedExceptionReason = reason;
        RestyleExceptionOptions();
    }

    // Same border-color-selected convention as JobDetailPage's inspection condition boxes -
    // the muted rows stay neutral, only the chosen reason takes the exception accent color.
    private void RestyleExceptionOptions()
    {
        var options = new (Border Border, Label Label, string Reason)[]
        {
            (PartialLoadOption, PartialLoadLabel, "PartialLoad"),
            (StockNotAvailableOption, StockNotAvailableLabel, "StockNotAvailable"),
            (CapacityExceededOption, CapacityExceededLabel, "VehicleCapacityExceeded"),
            (MaterialDamagedOption, MaterialDamagedLabel, "MaterialDamaged"),
            (WrongMaterialOption, WrongMaterialLabel, "WrongMaterialLoaded")
        };

        var mutedBorder = (Color)Application.Current!.Resources["CardBorderLight"];
        var mutedText = (Color)Application.Current.Resources["TextSecondaryLight"];
        var activeColor = (Color)Application.Current.Resources["StatusException"];

        foreach (var (border, label, reason) in options)
        {
            var selected = reason == _selectedExceptionReason;
            border.Stroke = selected ? activeColor : mutedBorder;
            border.StrokeThickness = selected ? 2 : 1.5;
            label.TextColor = selected ? activeColor : mutedText;
            label.FontFamily = selected ? "PoppinsBold" : "PoppinsSemiBold";
        }
    }

    private static string FriendlyExceptionReason(string? reason) => reason switch
    {
        "PartialLoad" => "Partial load",
        "StockNotAvailable" => "Stock not available",
        "VehicleCapacityExceeded" => "Vehicle capacity exceeded",
        "MaterialDamaged" => "Material damaged",
        "WrongMaterialLoaded" => "Wrong material loaded",
        _ => reason ?? string.Empty
    };

    private async void OnReportExceptionClicked(object? sender, EventArgs e)
    {
        if (_selectedExceptionReason is null)
        {
            await DisplayAlert("Select a reason", "Choose what kind of exception you're reporting.", "OK");
            return;
        }

        ReportExceptionButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            var remarks = string.IsNullOrWhiteSpace(ExceptionRemarksEntry.Text) ? null : ExceptionRemarksEntry.Text.Trim();
            _job = await ApiClient.ReportOutwardExceptionAsync(_jobId, _selectedExceptionReason, remarks);
            RenderJob();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not report exception", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            ReportExceptionButton.IsEnabled = true;
        }
    }

    private async void OnConfirmDispatchReadyClicked(object? sender, EventArgs e)
    {
        ConfirmDispatchReadyButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.ConfirmDispatchReadyAsync(_jobId);
            RenderJob();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not confirm", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            ConfirmDispatchReadyButton.IsEnabled = true;
        }
    }

    private async void OnCompleteClicked(object? sender, EventArgs e)
    {
        CompleteButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.CompleteOutwardAsync(_jobId);
            RenderJob();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not complete", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            CompleteButton.IsEnabled = _job?.Photos.Count > 0 && _job?.DispatchReadyConfirmedAt is not null;
        }
    }

    private async void OnRestartLoadingClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlert(
            "Restart loading?",
            "This reopens the job for loading confirmation again - every group goes back to Not Started. Continue?",
            "Restart", "Cancel");
        if (!confirmed)
        {
            return;
        }

        RestartLoadingButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.RestartLoadingAsync(_jobId);
            RenderJob();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not restart", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            RestartLoadingButton.IsEnabled = true;
        }
    }
}

