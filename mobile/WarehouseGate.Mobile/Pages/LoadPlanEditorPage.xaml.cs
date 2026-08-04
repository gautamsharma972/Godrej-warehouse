using System.Text.Json;
using WarehouseGate.Mobile.Controls;
using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

[QueryProperty(nameof(JobId), "id")]
public partial class LoadPlanEditorPage : ContentPage
{
    // Mirrors OutwardLoadPlanService.Palette so an un-placed SKU's swatch already shows the color
    // it will get once placed (index-by-distinct-line-order, same as the server's ColorForLine).
    private static readonly string[] Palette =
        { "#4f7cff", "#ff9f43", "#26c281", "#e0568c", "#8e6cff", "#ffcf44", "#3fbfbf", "#ff6b6b" };

    // Mirrors OutwardLoadPlanService's GetVehicleProfile fallback - a vehicle with no registered
    // capacity would otherwise send the viewport a 0x0x0 envelope (nothing renders at all) instead
    // of just an oversized placeholder truck.
    private const double FallbackVehicleLengthCm = 2000;
    private const double FallbackVehicleWidthCm = 300;
    private const double FallbackVehicleHeightCm = 300;

    // Adjacency order for the GroupToolbar's directional Move buttons - must match
    // OutwardLoadPlanService.ZoneFootprint's convention exactly.
    private static readonly string[] ZoneLengthOrder = { "Front", "Middle", "Back" };
    private static readonly string[] ZoneWidthOrder = { "Left", "Right" };

    private int _jobId;
    private OutwardJob? _job;
    private List<LoadPlanOptionSummary> _options = new();
    private int? _selectedOptionId;
    private List<LoadPlanGroup> _groups = new();
    // Camera mode of the viewport scene: "3d" | "top" | "side" | "sideRight" | "layer".
    private string _activeView = "3d";
    private bool? _isWide;
    private bool _drawerAnimated;

    private string? _lastVizPayload;
    private bool _vizResendBurstStarted;
    private int _vizResendCount;

    // Placement is create-only: dragging/tapping a SKU chip opens this popup to collect a
    // quantity (and, for a tap, a section) before calling CreateGroupAsync. Moving an
    // ALREADY-placed group between sections happens immediately via the GroupToolbar's
    // directional buttons (see MoveSelectedGroupAsync) - this popup never edits one.
    private bool _placementActive;
    private int? _pendingLineId;
    private string _pendingZoneLength = "Front";
    private string _pendingZoneWidth = "Left";
    private LoadGroupPreview? _lastPreview;

    private LoadPlanGroup? _selectedGroup;
    private bool _ruleValidationExpanded = true;

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

    public LoadPlanEditorPage()
    {
        InitializeComponent();
        LoadVizWebView.SetInvokeJavaScriptTarget(this);
        RestyleViewModeButtons();
        RestyleZoneButtons();
        ApplyRuleValidationExpansionState();
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

    // Full-screen drawer layout: the WebView fills the main panel, so only keep a practical
    // minimum height for narrow devices.
    private void ApplyResponsiveLayout(bool wide) =>
        LoadVizWebView.MinimumHeightRequest = wide ? 640 : 520;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetFlyoutBehavior(this, FlyoutBehavior.Disabled);
        _ = AnimateDrawerInAsync();
        _ = LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Session.IsSupervisor)
        {
            Shell.SetFlyoutBehavior(this, FlyoutBehavior.Locked);
        }
    }

    private async Task AnimateDrawerInAsync()
    {
        if (_drawerAnimated)
        {
            return;
        }

        _drawerAnimated = true;
        await Task.Yield();

        var width = Width > 0 ? Width : DeviceDisplay.Current.MainDisplayInfo.Width / DeviceDisplay.Current.MainDisplayInfo.Density;
        PageDrawer.TranslationX = width;
        PageDrawer.Opacity = 0.98;
        await Task.WhenAll(
            PageDrawer.TranslateTo(0, 0, 240, Easing.CubicOut),
            PageDrawer.FadeTo(1, 160, Easing.CubicOut));
    }

    private async Task LoadAsync()
    {
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.GetOutwardJobAsync(_jobId);

            // Every route into planning now lands here directly (home-page tap, the detail
            // page's redirect, dock-in), which can skip the old explicit "Start Loading" step -
            // but downstream steps hard-require status Loading (ConfirmDispatchReady, Complete)
            // and LoadingStartTime feeds the productivity KPIs. So a job arriving still Docked
            // is transitioned to Loading right here, the one funnel all paths share. Best-effort:
            // if it fails (e.g. not the assigned supervisor), the editor's own ownership errors
            // surface naturally on the next action instead.
            if (_job.Status == "Docked")
            {
                try
                {
                    _job = await ApiClient.StartLoadingAsync(_jobId);
                }
                catch (ApiException)
                {
                    // Leave the job as-is; planning is still viewable at Docked.
                }
            }

            PageHeaderView.PageTitle = $"{_job.DispatchOrderNumber} — {_job.CustomerName}";

            await RefreshOptionsAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Error", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    // Single refresh entry point for everything below the header - re-fetches
    // options, picks the selected one (or falls back to the first), re-fetches
    // its groups, and re-renders every section. Called after every mutation
    // (create/select/delete option, place/edit/delete group) rather than
    // hand-patching individual pieces of UI state.
    private async Task RefreshOptionsAsync()
    {
        _options = await ApiClient.GetLoadPlanOptionsAsync(_jobId);

        // A job with zero saved arrangements leaves every SKU chip disabled (nothing to place
        // into) with no explanation visible unless the supervisor happens to scroll to "Saved
        // arrangements" and tap + New first. Bootstrap "Option A" automatically instead, so SKUs
        // are placeable the moment this page opens - same auto-label/auto-select behavior as
        // tapping + New manually.
        if (_options.Count == 0)
        {
            try
            {
                await ApiClient.CreateLoadPlanOptionAsync(_jobId, null);
                _options = await ApiClient.GetLoadPlanOptionsAsync(_jobId);
            }
            catch (ApiException)
            {
                // Leave it to the manual "+ New" flow (e.g. the job isn't in an editable status).
            }
        }

        if (_options.Count == 0)
        {
            _selectedOptionId = null;
            _groups = new();
            await RefreshViewportAndWarningsAsync();
        }
        else
        {
            var selected = _options.FirstOrDefault(o => o.IsSelected) ?? _options[0];
            _selectedOptionId = selected.Id;

            // Groups and validation are independent - fetch them concurrently to shave a
            // round trip off every drop/move/delete refresh.
            var groupsTask = ApiClient.GetLoadPlanGroupsAsync(_jobId, selected.Id);
            var validationTask = ApiClient.ValidateLoadPlanOptionAsync(_jobId, selected.Id);
            _groups = await groupsTask;

            LoadPlanValidation? validation = null;
            try
            {
                validation = await validationTask;
            }
            catch (ApiException)
            {
                // Vehicle capacity may not be on file yet - render without stats.
            }

            await RefreshViewportAndWarningsAsync(validation);
        }

        StartConfirmationButton.IsEnabled = _selectedOptionId is not null && _groups.Count > 0;
    }

    // ---------- Saved arrangements: driven from the viewport's overlay rail (JS) ----------

    public Task OnOptionSelected(int optionId)
    {
        MainThread.BeginInvokeOnMainThread(async () => await SelectOptionAsync(optionId));
        return Task.CompletedTask;
    }

    public Task OnOptionDeleted(int optionId)
    {
        MainThread.BeginInvokeOnMainThread(async () => await DeleteOptionAsync(optionId));
        return Task.CompletedTask;
    }

    public Task OnNewOptionRequested()
    {
        MainThread.BeginInvokeOnMainThread(async () => await CreateNewOptionAsync());
        return Task.CompletedTask;
    }

    // "Compact load" in the viewport rail: run the per-zone gap-close pass on demand,
    // for tidying an arrangement without having to delete something first.
    public Task OnCompactRequested()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (_selectedOptionId is null)
            {
                return;
            }

            Spinner.IsVisible = true;
            Spinner.IsRunning = true;
            try
            {
                await ApiClient.CompactLoadPlanGroupsAsync(_jobId, _selectedOptionId.Value);
                await RefreshOptionsAsync();
            }
            catch (ApiException ex)
            {
                await DisplayAlert("Could not compact", ex.Message, "OK");
            }
            finally
            {
                Spinner.IsVisible = false;
                Spinner.IsRunning = false;
            }
        });
        return Task.CompletedTask;
    }

    private async Task SelectOptionAsync(int optionId)
    {
        if (optionId == _selectedOptionId)
        {
            return;
        }

        ClosePlacement();
        CloseSelection();

        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            await ApiClient.SelectLoadPlanOptionAsync(_jobId, optionId);
            await RefreshOptionsAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not switch option", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    private async Task DeleteOptionAsync(int optionId)
    {
        var confirmed = await DisplayAlert("Delete option", "Delete this saved arrangement and all its placed groups?", "Delete", "Cancel");
        if (!confirmed)
        {
            return;
        }

        ClosePlacement();
        CloseSelection();

        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            await ApiClient.DeleteLoadPlanOptionAsync(_jobId, optionId);
            await RefreshOptionsAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not delete", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    private async Task CreateNewOptionAsync()
    {
        ClosePlacement();
        CloseSelection();

        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            var created = await ApiClient.CreateLoadPlanOptionAsync(_jobId, null);
            await ApiClient.SelectLoadPlanOptionAsync(_jobId, created.Id);
            await RefreshOptionsAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not create option", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    // The SKU list renders as an HTML overlay inside the 3D viewport (see loadviz.js's
    // renderSkuRail, fed by the skus array in the render payload) - these are the two
    // callbacks its chips invoke. Tap = open the popup with a default section (Front-Left)
    // pre-selected, editable before confirming. Drag onto one of the 6 floor zones = open
    // the same popup with THAT section pre-selected instead.
    public Task OnSkuChipTapped(int lineId)
    {
        MainThread.BeginInvokeOnMainThread(() => StartPlacingNew(lineId, "Front", "Left"));
        return Task.CompletedTask;
    }

    public Task OnSkuChipDropped(int lineId, string zoneLength, string zoneWidth)
    {
        MainThread.BeginInvokeOnMainThread(() => StartPlacingNew(lineId, zoneLength, zoneWidth));
        return Task.CompletedTask;
    }

    // ---------- Placing a new group ----------

    private void StartPlacingNew(int lineId, string zoneLength, string zoneWidth)
    {
        if (_selectedOptionId is null || _job is null)
        {
            _ = DisplayAlert("No arrangement selected", "Tap \"+ New\" to start a saved arrangement first.", "OK");
            return;
        }

        CloseSelection();

        var line = _job.Lines.First(l => l.Id == lineId);
        var placedByOthers = _groups.Where(g => g.DispatchOrderLineId == lineId).Sum(g => g.Quantity);
        var remaining = (int)Math.Max(0, line.OrderedQty - placedByOthers);

        _placementActive = true;
        _pendingLineId = lineId;
        _pendingZoneLength = zoneLength;
        _pendingZoneWidth = zoneWidth;
        _lastPreview = null;

        PlacementSkuLabel.Text = line.ProductName;
        PlacementQtyEntry.Text = remaining.ToString();
        RestyleZoneButtons();
        PlacementSummaryLabel.Text = "Pick a section and quantity, then confirm.";
        PlacementWarningsContainer.Children.Clear();
        ConfirmPlacementButton.IsEnabled = false;

        PlacementPanel.IsVisible = true;
        ViewportHintLabel.IsVisible = false;
        _ = RefreshPlacementPreviewAsync();
        _ = HighlightPlacementCardAsync();
    }

    // Draws the eye to the quantity-entry card the moment it appears from a SKU drag/drop or
    // tap - scrolls it into view (it can be off-screen if the supervisor was scrolled down to
    // Load health/Floor confirmation) and pulses its border so it isn't missed.
    private async Task HighlightPlacementCardAsync()
    {
        try
        {
            await CenterScrollView.ScrollToAsync(PlacementPanel, ScrollToPosition.Start, true);
        }
        catch
        {
            // Best-effort - scrolling shouldn't block opening the panel.
        }

        var accent = (Color)Application.Current!.Resources["Primary"];
        var normalStroke = (Color)Application.Current.Resources["CardBorderLight"];
        PlacementCardBorder.Stroke = accent;
        PlacementCardBorder.StrokeThickness = 2;
        await PlacementCardBorder.ScaleTo(1.02, 120, Easing.CubicOut);
        await PlacementCardBorder.ScaleTo(1.0, 120, Easing.CubicIn);
        await Task.Delay(450);

        // A rapid re-drop while the fade-back is still running would otherwise have this
        // delayed reset clobber the NEXT highlight's active state.
        if (_placementActive)
        {
            PlacementCardBorder.Stroke = normalStroke;
            PlacementCardBorder.StrokeThickness = 1;
        }
    }

    private void OnCancelPlacementClicked(object? sender, EventArgs e) => ClosePlacement();

    private void ClosePlacement()
    {
        _placementActive = false;
        _pendingLineId = null;
        _lastPreview = null;
        PlacementPanel.IsVisible = false;
        ViewportHintLabel.IsVisible = true;
        _ = RefreshViewportAndWarningsAsync();
    }

    private void OnPlacementQtyMinusClicked(object? sender, EventArgs e) => AdjustPlacementQty(-1);
    private void OnPlacementQtyPlusClicked(object? sender, EventArgs e) => AdjustPlacementQty(1);

    private void AdjustPlacementQty(int delta)
    {
        if (!int.TryParse(PlacementQtyEntry.Text, out var qty))
        {
            qty = 0;
        }
        PlacementQtyEntry.Text = Math.Max(0, qty + delta).ToString();
    }

    private void OnPlacementFieldChanged(object? sender, TextChangedEventArgs e) => _ = RefreshPlacementPreviewAsync();

    // The 6-button section picker - single-select, same restyle pattern as the old
    // orientation/grid-snap chip rows.
    private (Button Button, string ZoneLength, string ZoneWidth)[] ZoneButtons => new[]
    {
        (ZoneFrontLeftButton, "Front", "Left"),
        (ZoneFrontRightButton, "Front", "Right"),
        (ZoneMiddleLeftButton, "Middle", "Left"),
        (ZoneMiddleRightButton, "Middle", "Right"),
        (ZoneBackLeftButton, "Back", "Left"),
        (ZoneBackRightButton, "Back", "Right"),
    };

    private void OnZoneButtonClicked(object? sender, EventArgs e)
    {
        foreach (var (button, zoneLength, zoneWidth) in ZoneButtons)
        {
            if (ReferenceEquals(button, sender))
            {
                _pendingZoneLength = zoneLength;
                _pendingZoneWidth = zoneWidth;
                break;
            }
        }
        RestyleZoneButtons();
        _ = RefreshPlacementPreviewAsync();
    }

    private void RestyleZoneButtons()
    {
        var active = (Color)Application.Current!.Resources["Primary"];
        var mutedBorder = (Color)Application.Current.Resources["CardBorderLight"];
        var mutedText = (Color)Application.Current.Resources["TextPrimaryLight"];

        foreach (var (button, zoneLength, zoneWidth) in ZoneButtons)
        {
            var selected = zoneLength == _pendingZoneLength && zoneWidth == _pendingZoneWidth;
            button.BackgroundColor = selected ? active : Colors.Transparent;
            button.TextColor = selected ? Colors.White : mutedText;
            button.BorderColor = selected ? active : mutedBorder;
        }
    }

    private bool TryBuildPlacementInput(out PlaceLoadGroupInput input, out string? error)
    {
        input = new PlaceLoadGroupInput();
        error = null;

        if (_pendingLineId is null)
        {
            error = "Choose a SKU first.";
            return false;
        }

        if (!int.TryParse(PlacementQtyEntry.Text, out var qty) || qty <= 0)
        {
            error = "Enter a quantity greater than zero.";
            return false;
        }

        input = new PlaceLoadGroupInput
        {
            DispatchOrderLineId = _pendingLineId.Value,
            Quantity = qty,
            ZoneLength = _pendingZoneLength,
            ZoneWidth = _pendingZoneWidth
        };
        return true;
    }

    private async Task RefreshPlacementPreviewAsync()
    {
        if (_pendingLineId is null || _selectedOptionId is null)
        {
            _lastPreview = null;
            await RefreshViewportAndWarningsAsync();
            return;
        }

        if (!TryBuildPlacementInput(out var input, out var error))
        {
            _lastPreview = null;
            PlacementSummaryLabel.Text = error;
            ConfirmPlacementButton.IsEnabled = false;
            await RefreshViewportAndWarningsAsync();
            return;
        }

        try
        {
            var preview = await ApiClient.PreviewLoadPlanGroupAsync(_jobId, _selectedOptionId.Value, input);
            _lastPreview = preview;
            ConfirmPlacementButton.IsEnabled = preview.IsValid && preview.PlacedCount > 0;
            PlacementSummaryLabel.Text = preview.IsValid
                ? $"Will place {preview.PlacedCount} cartons here ({preview.ResolvedRows}x{preview.ResolvedColumns}x{preview.ResolvedLayers} rows x columns x layers)."
                : "Can't place here - see warning below.";

            PlacementWarningsContainer.Children.Clear();
            foreach (var warning in preview.Warnings)
            {
                PlacementWarningsContainer.Children.Add(new Label
                {
                    Text = "⚠ " + warning,
                    FontSize = 11,
                    TextColor = (Color)Application.Current!.Resources["StatusException"]
                });
            }
        }
        catch (ApiException ex)
        {
            _lastPreview = null;
            ConfirmPlacementButton.IsEnabled = false;
            PlacementSummaryLabel.Text = ex.Message;
        }

        await RefreshViewportAndWarningsAsync();
    }

    private async void OnConfirmPlacementClicked(object? sender, EventArgs e)
    {
        if (_selectedOptionId is null)
        {
            await DisplayAlert("Check your entries", "No arrangement selected.", "OK");
            return;
        }

        if (!TryBuildPlacementInput(out var input, out var error))
        {
            await DisplayAlert("Check your entries", error ?? "Missing information.", "OK");
            return;
        }

        ConfirmPlacementButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            await ApiClient.CreateLoadPlanGroupAsync(_jobId, _selectedOptionId.Value, input);
            ClosePlacement();
            await RefreshOptionsAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not save", ex.Message, "OK");
            ConfirmPlacementButton.IsEnabled = true;
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    // ---------- Selecting an already-placed group ----------

    private void SelectGroup(LoadPlanGroup group)
    {
        ClosePlacement();
        _selectedGroup = group;
        SelectedGroupLabel.Text = $"{group.ProductName} — {group.Quantity} cartons{(group.IsLocked ? " (locked)" : "")}";
        LockGroupButton.Text = group.IsLocked ? "Unlock" : "Lock";
        GroupToolbar.IsVisible = true;
        _ = RefreshViewportAndWarningsAsync();
    }

    private void CloseSelection()
    {
        _selectedGroup = null;
        GroupToolbar.IsVisible = false;
    }

    private void OnCloseSelectionClicked(object? sender, EventArgs e) => CloseSelection();

    // ---------- Moving a selected group between adjacent sections ----------

    private async void OnMoveForwardClicked(object? sender, EventArgs e) => await MoveSelectedGroupAsync(lengthStep: -1, widthStep: 0);
    private async void OnMoveBackClicked(object? sender, EventArgs e) => await MoveSelectedGroupAsync(lengthStep: 1, widthStep: 0);
    private async void OnMoveLeftClicked(object? sender, EventArgs e) => await MoveSelectedGroupAsync(lengthStep: 0, widthStep: -1);
    private async void OnMoveRightClicked(object? sender, EventArgs e) => await MoveSelectedGroupAsync(lengthStep: 0, widthStep: 1);

    private async Task MoveSelectedGroupAsync(int lengthStep, int widthStep)
    {
        var group = _selectedGroup;
        if (group is null || _selectedOptionId is null)
        {
            return;
        }

        if (group.IsLocked)
        {
            await DisplayAlert("Locked", "Unlock this group before moving it.", "OK");
            return;
        }

        var lengthIndex = Array.IndexOf(ZoneLengthOrder, group.ZoneLength);
        var widthIndex = Array.IndexOf(ZoneWidthOrder, group.ZoneWidth);
        var newLengthIndex = Math.Clamp(lengthIndex + lengthStep, 0, ZoneLengthOrder.Length - 1);
        var newWidthIndex = Math.Clamp(widthIndex + widthStep, 0, ZoneWidthOrder.Length - 1);

        if (newLengthIndex == lengthIndex && newWidthIndex == widthIndex)
        {
            var edge = lengthStep < 0 ? "the front" : lengthStep > 0 ? "the back" : widthStep < 0 ? "the left" : "the right";
            await DisplayAlert("Can't move further", $"This group is already at {edge}.", "OK");
            return;
        }

        var groupId = group.Id;
        var optionId = _selectedOptionId.Value;

        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            var input = new PlaceLoadGroupInput
            {
                DispatchOrderLineId = group.DispatchOrderLineId,
                Quantity = group.Quantity,
                ZoneLength = ZoneLengthOrder[newLengthIndex],
                ZoneWidth = ZoneWidthOrder[newWidthIndex]
            };
            await ApiClient.UpdateLoadPlanGroupAsync(_jobId, optionId, groupId, input);
            await RefreshOptionsAsync();

            // Keep the toolbar open on the moved group so repeated taps keep moving it.
            var moved = _groups.FirstOrDefault(g => g.Id == groupId);
            if (moved is not null)
            {
                SelectGroup(moved);
            }
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not move", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    private async void OnDuplicateGroupClicked(object? sender, EventArgs e)
    {
        if (_selectedGroup is null || _selectedOptionId is null)
        {
            return;
        }

        var groupId = _selectedGroup.Id;
        CloseSelection();
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            await ApiClient.DuplicateLoadPlanGroupAsync(_jobId, _selectedOptionId.Value, groupId);
            await RefreshOptionsAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not duplicate", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    private async void OnSplitGroupClicked(object? sender, EventArgs e)
    {
        if (_selectedGroup is null || _selectedOptionId is null)
        {
            return;
        }

        if (_selectedGroup.Quantity < 2)
        {
            await DisplayAlert("Can't split", "This group only has 1 carton - nothing to split off.", "OK");
            return;
        }

        var input = await DisplayPromptAsync("Split by quantity",
            $"How many of the {_selectedGroup.Quantity} cartons should split off into a new group in this same section?",
            "Split", "Cancel", keyboard: Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(input) || !int.TryParse(input, out var splitQty) || splitQty <= 0 || splitQty >= _selectedGroup.Quantity)
        {
            return;
        }

        var groupId = _selectedGroup.Id;
        CloseSelection();
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            await ApiClient.SplitLoadPlanGroupAsync(_jobId, _selectedOptionId.Value, groupId, splitQty);
            await RefreshOptionsAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not split", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    private async void OnToggleLockGroupClicked(object? sender, EventArgs e)
    {
        if (_selectedGroup is null || _selectedOptionId is null)
        {
            return;
        }

        var groupId = _selectedGroup.Id;
        var newLocked = !_selectedGroup.IsLocked;
        CloseSelection();
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            await ApiClient.SetLoadPlanGroupLockAsync(_jobId, _selectedOptionId.Value, groupId, newLocked);
            await RefreshOptionsAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not update lock", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    private async void OnDeleteGroupClicked(object? sender, EventArgs e)
    {
        if (_selectedGroup is null || _selectedOptionId is null)
        {
            return;
        }

        var confirmed = await DisplayAlert("Delete group", "Remove this placed group from the plan?", "Delete", "Cancel");
        if (!confirmed)
        {
            return;
        }

        var groupId = _selectedGroup.Id;
        CloseSelection();
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            await ApiClient.DeleteLoadPlanGroupAsync(_jobId, _selectedOptionId.Value, groupId);
            await RefreshOptionsAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not delete", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    public Task OnGroupTapped(int groupId)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var group = _groups.FirstOrDefault(g => g.Id == groupId);
            if (group is not null)
            {
                SelectGroup(group);
            }
        });
        return Task.CompletedTask;
    }

    // ---------- Viewport + warnings ----------

    // One switcher for the whole viewport - all four are camera modes of the same
    // interactive WebView scene (3D orbits; Top/Side/Layer are fixed presets that pan,
    // with Layer adding a height-band filter). SKU chips, drag-to-place, group taps
    // and the side-rail overlays keep working identically in every mode.
    private async void OnViewMode3DClicked(object? sender, EventArgs e) => await SetActiveViewAsync("3d");
    private async void OnViewModeTopClicked(object? sender, EventArgs e) => await SetActiveViewAsync("top");
    private async void OnViewModeSideClicked(object? sender, EventArgs e) => await SetActiveViewAsync("side");
    private async void OnViewModeSideRightClicked(object? sender, EventArgs e) => await SetActiveViewAsync("sideRight");
    private async void OnViewModeLayerClicked(object? sender, EventArgs e) => await SetActiveViewAsync("layer");

    private async Task SetActiveViewAsync(string view)
    {
        _activeView = view;
        RestyleViewModeButtons();
        await RefreshViewportAndWarningsAsync();
    }

    private void RestyleViewModeButtons()
    {
        var active = (Color)Application.Current!.Resources["Primary"];
        var mutedBorder = (Color)Application.Current.Resources["CardBorderLight"];
        var mutedText = (Color)Application.Current.Resources["TextPrimaryLight"];

        foreach (var (button, mode) in new[] { (ViewMode3DButton, "3d"), (ViewModeTopButton, "top"), (ViewModeSideButton, "side"), (ViewModeSideRightButton, "sideRight"), (ViewModeLayerButton, "layer") })
        {
            var selected = mode == _activeView;
            button.BackgroundColor = selected ? active : Colors.Transparent;
            button.TextColor = selected ? Colors.White : mutedText;
            button.BorderColor = selected ? active : mutedBorder;
        }
    }

    private async Task RefreshViewportAndWarningsAsync(LoadPlanValidation? prefetchedValidation = null)
    {
        if (_job is null)
        {
            return;
        }

        object? preview = null;
        if (_placementActive && _lastPreview is { PlacedCount: > 0 })
        {
            preview = new
            {
                x = _lastPreview.BoundingBoxX,
                y = _lastPreview.BoundingBoxY,
                z = _lastPreview.BoundingBoxZ,
                w = _lastPreview.BoundingBoxDimX,
                h = _lastPreview.BoundingBoxDimY,
                d = _lastPreview.BoundingBoxDimZ
            };
        }

        // SKU chips render inside the WebView's own overlay (loadviz.js renderSkuRail) so
        // dragging one onto the truck never has to cross a native/WebView boundary.
        var distinctLineIds = _job.Lines.Select(l => l.Id).Distinct().ToList();
        var skus = _job.Lines.Select(line =>
        {
            var placedQty = _groups.Where(g => g.DispatchOrderLineId == line.Id).Sum(g => g.Quantity);
            var remaining = (int)Math.Max(0, line.OrderedQty - placedQty);
            var paletteIndex = distinctLineIds.IndexOf(line.Id);
            var color = !string.IsNullOrWhiteSpace(line.ColorHex)
                ? line.ColorHex!
                : Palette[paletteIndex < 0 ? 0 : paletteIndex % Palette.Length];
            return new
            {
                lineId = line.Id,
                name = line.ProductName,
                code = line.SkuCode ?? "",
                color,
                remaining,
                enabled = _selectedOptionId is not null && remaining > 0,
                // Unit carton dims (cm) so the drag ghost can render as a real box.
                l = (double)(line.LengthCm ?? 10),
                w = (double)(line.WidthCm ?? 10),
                h = (double)(line.HeightCm ?? 10)
            };
        }).ToList();

        // Data for the viewport's right-rail overlays: the saved-arrangement comparison
        // chips and the placed-groups/search list (search filters this locally in JS by
        // name, SKU code, or delivery location - no extra API round trip needed).
        var options = _options.Select(o => new
        {
            id = o.Id,
            label = o.Label,
            isSelected = o.Id == _selectedOptionId,
            groupCount = o.GroupCount,
            spacePct = o.Simulation.VehicleUtilizationPct,
            weightPct = o.Simulation.WeightUtilizationPct,
            warnCount = o.WarningCount
        }).ToList();

        // One entry per GROUP (not per carton): the JS side derives per-carton geometry
        // itself, which keeps this payload ~50x smaller than shipping 1000+ carton boxes
        // across the WebView bridge on every refresh.
        var groupsList = _groups.OrderBy(g => g.LoadSequence).Select(g =>
        {
            var line = _job.Lines.FirstOrDefault(l => l.Id == g.DispatchOrderLineId);
            return new
            {
                groupId = g.Id,
                name = g.ProductName,
                qty = g.Quantity,
                color = g.Color,
                locked = g.IsLocked,
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
        }).ToList();

        var payload = new
        {
            vehicle = new
            {
                widthCm = (double?)_job.VehicleWidthCm ?? FallbackVehicleWidthCm,
                lengthCm = (double?)_job.VehicleLengthCm ?? FallbackVehicleLengthCm,
                heightCm = (double?)_job.VehicleHeightCm ?? FallbackVehicleHeightCm,
                number = _job.VehicleNumber ?? "",
                typeLabel = _job.VehicleMaxWeightKg is null ? "" : $"~{Math.Round(_job.VehicleMaxWeightKg.Value / 1000)} Ton Truck"
            },
            unplacedNames = Array.Empty<string>(),
            viewMode = _activeView,
            placementActive = _placementActive,
            preview,
            previewValid = _lastPreview?.IsValid ?? true,
            previewMessage = _lastPreview?.Warnings.FirstOrDefault() ?? "",
            skus,
            options,
            groupsList
        };

        _lastVizPayload = JsonSerializer.Serialize(payload);
        LoadVizWebView.SendRawMessage(_lastVizPayload);
        StartVizResendBurstIfNeeded();

        if (_selectedOptionId is null)
        {
            WarningsContainer.Children.Clear();
            WeightUtilLabel.Text = "—";
            SpaceUtilLabel.Text = "—";
            WeightUtilBar.Progress = 0;
            SpaceUtilBar.Progress = 0;
            return;
        }

        if (prefetchedValidation is not null)
        {
            RenderWarnings(prefetchedValidation);
            return;
        }

        try
        {
            var validation = await ApiClient.ValidateLoadPlanOptionAsync(_jobId, _selectedOptionId.Value);
            RenderWarnings(validation);
        }
        catch (ApiException)
        {
            // Vehicle capacity may not be on file yet - leave stats blank rather than blocking the page.
        }
    }

    // Collapse/expand the whole "Rule validation" card - tapping its header or chevron toggles
    // WarningsCollapsibleContent's visibility. The server (LoadPlanValidator) already dedupes
    // each rule's own warnings to one message per SKU/condition; ".Distinct()" here is just a
    // defensive backstop against any future rule that isn't as careful.
    private void OnToggleRuleValidationClicked(object? sender, EventArgs e)
    {
        _ruleValidationExpanded = !_ruleValidationExpanded;
        ApplyRuleValidationExpansionState();
    }

    private void ApplyRuleValidationExpansionState()
    {
        WarningsCollapsibleContent.IsVisible = _ruleValidationExpanded;
        RuleValidationChevron.Text = _ruleValidationExpanded ? IconGlyphs.ChevronUp : IconGlyphs.ChevronDown;
    }

    private void RenderWarnings(LoadPlanValidation validation)
    {
        WarningsContainer.Children.Clear();
        var uniqueWarnings = validation.Warnings
            .Select(w => w.Message)
            .Distinct()
            .ToList();
        RuleValidationSubtitleLabel.Text = uniqueWarnings.Count == 0
            ? "Active arrangement notes"
            : uniqueWarnings.Count == 1 ? "1 issue found" : $"{uniqueWarnings.Count} issues found";
        if (uniqueWarnings.Count == 0)
        {
            WarningsContainer.Children.Add(new Label
            {
                Text = "No issues found.",
                FontSize = 12,
                TextColor = (Color)Application.Current!.Resources["StatusSuccess"]
            });
        }
        else
        {
            foreach (var message in uniqueWarnings)
            {
                WarningsContainer.Children.Add(new Label
                {
                    Text = "⚠ " + message,
                    FontSize = 12,
                    TextColor = (Color)Application.Current!.Resources["StatusException"]
                });
            }
        }

        WeightUtilLabel.Text = $"{validation.Simulation.WeightUtilizationPct:0.#}%";
        WeightUtilBar.Progress = Math.Clamp(validation.Simulation.WeightUtilizationPct / 100, 0, 1);
        SpaceUtilLabel.Text = $"{validation.Simulation.VehicleUtilizationPct:0.#}%";
        SpaceUtilBar.Progress = Math.Clamp(validation.Simulation.VehicleUtilizationPct / 100, 0, 1);

        var boxesPlaced = _groups.Sum(g => g.Quantity);
        var remaining = (_job?.Lines ?? new List<DispatchOrderLine>()).Sum(line =>
        {
            var placedForLine = _groups.Where(g => g.DispatchOrderLineId == line.Id).Sum(g => g.Quantity);
            return (int)Math.Max(0, line.OrderedQty - placedForLine);
        });

        BoxesPlacedLabel.Text = boxesPlaced.ToString();
        RemainingLabel.Text = remaining.ToString();
        FreeVolumeLabel.Text = $"{validation.Simulation.RemainingVolumeM3:0.#}";
        WarningCountLabel.Text = validation.Warnings.Count.ToString();

        BalanceStatusLabel.Text = validation.Simulation.BalanceStatus;
        var balanceBadge = (Border)BalanceStatusLabel.Parent;
        balanceBadge.BackgroundColor = validation.Simulation.BalanceStatus == "Balanced"
            ? (Color)Application.Current!.Resources["StatusSuccess"]
            : (Color)Application.Current!.Resources["StatusAssigned"];
    }

    // Same resend-burst workaround as OutwardJobDetailPage/LoadPlannerPage -
    // HybridWebView has no reliable "ready" event.
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

    private async void OnStartConfirmationClicked(object? sender, EventArgs e)
    {
        StartConfirmationButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            await ApiClient.StartLoadPlanConfirmationAsync(_jobId);
            await Shell.Current.GoToAsync($"{nameof(LoadConfirmationPage)}?id={_jobId}");
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not start confirmation", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            StartConfirmationButton.IsEnabled = true;
        }
    }

    // Lets a Supervisor load stock beyond the original pick list, once "Free m3" on this page
    // shows there's still spare vehicle space - reuses the same SKU Master picker Inward's
    // "Add Unplanned Line" uses, then a simple quantity prompt. The added line flows through the
    // normal LoadAsync/RefreshOptionsAsync refresh afterward, so it shows up as just another
    // placeable SKU chip in the viewport without any changes to the 3D rendering itself.
    private async void OnAddSkuClicked(object? sender, EventArgs e)
    {
        var tcs = new TaskCompletionSource<SkuMasterItem?>();
        await Navigation.PushModalAsync(new SkuPickerPage(result => tcs.TrySetResult(result)));
        var sku = await tcs.Task;
        if (sku is null)
        {
            return;
        }

        var quantityText = await DisplayPromptAsync(
            "Add SKU", $"How many cartons of {sku.Name} to add?", "Add", "Cancel", keyboard: Keyboard.Numeric);
        if (string.IsNullOrWhiteSpace(quantityText) || !decimal.TryParse(quantityText, out var quantity) || quantity <= 0)
        {
            return;
        }

        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            await ApiClient.AddOutwardDispatchLineAsync(_jobId, sku.Id, quantity);
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not add SKU", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }
}
