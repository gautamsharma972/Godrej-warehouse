using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;
using System.Globalization;

namespace WarehouseGate.Mobile.Pages;

[QueryProperty(nameof(JobId), "id")]
public partial class LoadConfirmationPage : ContentPage
{
    private int _jobId;
    private OutwardJob? _job;
    private readonly Dictionary<int, string> _localPhotoPaths = new();
    private readonly List<LoadLineRow> _loadLineRows = new();
    private List<LoadConfirmationStep> _lastSteps = new();

    private static readonly string[] ShortReasons =
    {
        "Stock not available", "Vehicle capacity exceeded", "Material damaged"
    };

    private class LoadLineRow
    {
        public required DispatchOrderLine Line { get; init; }
        public required Entry QtyEntry { get; init; }
        public required Entry NotesEntry { get; init; }
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

    public LoadConfirmationPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Shell.SetFlyoutBehavior(this, Session.IsSupervisor ? FlyoutBehavior.Locked : FlyoutBehavior.Disabled);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.GetOutwardJobAsync(_jobId);
            HeaderLabel.Text = $"{_job.DispatchOrderNumber} — {_job.CustomerName}";

            var steps = await ApiClient.GetLoadPlanConfirmationStepsAsync(_jobId);
            RenderSteps(steps);
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

    private void RenderSteps(List<LoadConfirmationStep> steps)
    {
        _lastSteps = steps;
        StepsContainer.Children.Clear();
        NoStepsLabel.IsVisible = steps.Count == 0;

        var resolvedCount = steps.Count(IsResolvedStep);
        var progressPercent = steps.Count == 0 ? 0 : (int)Math.Round((double)resolvedCount / steps.Count * 100);
        ProgressLabel.Text = steps.Count == 0 ? string.Empty : $"{resolvedCount} of {steps.Count} confirmed";
        ProgressBar.Progress = steps.Count == 0 ? 0 : (double)resolvedCount / steps.Count;

        var pendingCount = steps.Count - resolvedCount;
        StartLoadingAllButton.IsVisible = _job?.Status != "Completed" && pendingCount > 0;
        StartLoadingAllButton.Text = $"Start Loading  {progressPercent}%";

        if (steps.Count > 0)
        {
            StepsContainer.Children.Add(BuildStepsCarousel(steps));
        }

        RenderFinalSection(steps);
    }

    private View BuildStepsCarousel(List<LoadConfirmationStep> steps)
    {
        var cardsRow = new HorizontalStackLayout
        {
            Spacing = 12,
            Padding = new Thickness(0, 0, 4, 4)
        };

        foreach (var step in steps)
        {
            cardsRow.Children.Add(BuildStepCard(step));
        }

        return new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = cardsRow
        };
    }

    // Header-level "confirm everything" shortcut - marks every still-pending group Loaded at its
    // planned quantity in one shot. A supervisor who needs to flag a mismatch/short/skip on a
    // specific group instead does that from its row under "Load lines" (unaffected by this).
    private async void OnStartLoadingAllClicked(object? sender, EventArgs e)
    {
        var pending = _lastSteps.Where(s => !IsResolvedStep(s)).ToList();
        if (pending.Count == 0)
        {
            return;
        }

        var confirm = await DisplayAlert("Start loading",
            $"Mark all {pending.Count} pending group(s) as loaded at their planned quantity?", "Yes", "Cancel");
        if (!confirm)
        {
            return;
        }

        StartLoadingAllButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            foreach (var step in pending)
            {
                await ApiClient.MarkLoadPlanGroupLoadedAsync(_jobId, step.GroupId, null);
            }

            var steps = await ApiClient.GetLoadPlanConfirmationStepsAsync(_jobId);
            RenderSteps(steps);
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not start loading", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            StartLoadingAllButton.IsEnabled = true;
        }
    }

    // Load lines / photo evidence / exceptions / dispatch-ready / Complete are always reachable
    // now (not gated behind resolving every group first) - the per-group Loaded/Mismatch/Short/
    // Skip/Photo actions live inside the Load lines cards below, so this section has to be visible
    // for a supervisor to even reach them.
    private void RenderFinalSection(List<LoadConfirmationStep> steps)
    {
        if (_job is null)
        {
            FinalSection.IsVisible = false;
            return;
        }

        var job = _job;
        FinalSection.IsVisible = job.Lines.Count > 0;
        if (!FinalSection.IsVisible)
        {
            return;
        }

        var readOnly = job.Status == "Completed";
        var loadLinesRecorded = job.LoadLines.Count > 0;
        // Jobs with no 3D load plan at all (legacy flat flow) have nothing to resolve here -
        // don't block Complete on a set of groups that will never exist.
        var allGroupsResolved = steps.Count == 0 || steps.All(IsResolvedStep);

        BuildLoadLineRows(job, readOnly, steps);
        LoadLinesSavedBanner.IsVisible = loadLinesRecorded;

        var photoLimitReached = job.Photos.Count >= MaxPhotosPerJob;
        PhotoCountLabel.Text = job.Photos.Count == 1 ? "1 photo" : $"{job.Photos.Count} photos";
        CapturePhotoButton.IsVisible = !readOnly;
        CapturePhotoButton.IsEnabled = !readOnly && !photoLimitReached;
        PhotoLimitLabel.IsVisible = !readOnly && photoLimitReached;
        RenderPhotos(job);

        var dispatchReady = job.DispatchReadyConfirmedAt is not null;
        ConfirmDispatchReadyButton.IsVisible = !dispatchReady;
        DispatchReadyConfirmedBanner.IsVisible = dispatchReady;
        if (dispatchReady)
        {
            DispatchReadyConfirmedLabel.Text = $"Dispatch ready — confirmed {job.DispatchReadyConfirmedAt:g}";
        }

        CompleteButton.IsVisible = true;
        CompleteButton.IsEnabled = !readOnly && allGroupsResolved && job.Photos.Count > 0 && dispatchReady;
        CompleteHintLabel.IsVisible = !readOnly && (!allGroupsResolved || job.Photos.Count == 0 || !dispatchReady);
        CompleteHintLabel.Text = !allGroupsResolved
            ? "Resolve every loading group above before completing."
            : !dispatchReady
            ? "Confirm dispatch readiness before completing."
            : "Add at least one photo before completing.";
    }

    private void BuildLoadLineRows(OutwardJob job, bool readOnly, List<LoadConfirmationStep> steps)
    {
        LoadLinesContainer.Children.Clear();
        _loadLineRows.Clear();

        var stepsByLine = steps
            .GroupBy(s => s.DispatchOrderLineId)
            .ToDictionary(g => g.Key, g => g.OrderBy(s => s.StepNumber).ToList());

        var confirmedByLine = steps
            .Where(IsResolvedStep)
            .GroupBy(s => s.DispatchOrderLineId)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Quantity = g.Sum(ResolvedQuantity),
                    FirstStep = g.Min(s => s.StepNumber),
                    Notes = string.Join("; ", g.Select(s => s.ActualNotes).Where(n => !string.IsNullOrWhiteSpace(n)))
                });

        var orderedLines = job.Lines
            .Select((line, index) => new
            {
                Line = line,
                Existing = job.LoadLines.FirstOrDefault(l => l.DispatchOrderLineId == line.Id),
                StepOrder = confirmedByLine.TryGetValue(line.Id, out var confirmed) ? confirmed.FirstStep : int.MaxValue,
                Index = index
            })
            .OrderBy(x => x.Existing?.LoadSequence ?? x.StepOrder)
            .ThenBy(x => x.Index)
            .ToList();

        var cardRail = new HorizontalStackLayout
        {
            Spacing = 14,
            Padding = new Thickness(0, 0, 4, 4)
        };

        foreach (var item in orderedLines)
        {
            confirmedByLine.TryGetValue(item.Line.Id, out var confirmed);
            var loadedQty = item.Existing?.LoadedQty ?? confirmed?.Quantity ?? item.Line.OrderedQty;
            var notes = item.Existing?.Notes ?? confirmed?.Notes ?? string.Empty;
            stepsByLine.TryGetValue(item.Line.Id, out var lineSteps);
            var lineIsResolved = lineSteps is { Count: > 0 } && lineSteps.All(IsResolvedStep);
            var lineIsLocked = !readOnly && lineIsResolved;

            var qtyEntry = new Entry
            {
                Keyboard = Keyboard.Numeric,
                Text = loadedQty.ToString(CultureInfo.InvariantCulture),
                IsEnabled = !readOnly && !lineIsLocked,
                BackgroundColor = Colors.Transparent,
                FontFamily = "PoppinsSemiBold",
                FontSize = 16,
                TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"],
                Margin = new Thickness(0, -4, 0, -6)
            };

            var notesEntry = new Entry
            {
                Placeholder = "Notes (optional)",
                Text = notes,
                IsEnabled = !readOnly && !lineIsLocked,
                BackgroundColor = Colors.Transparent,
                FontFamily = "PoppinsRegular",
                FontSize = 13,
                TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"],
                Margin = new Thickness(0, -4, 0, -6)
            };

            var qtyMinus = BuildTinyQtyButton(IconGlyphs.Minus, !readOnly && !lineIsLocked);
            var qtyPlus = BuildTinyQtyButton("+", !readOnly && !lineIsLocked);
            qtyMinus.Clicked += (_, _) => AdjustQty(qtyEntry, -1);
            qtyPlus.Clicked += (_, _) => AdjustQty(qtyEntry, 1);

            var title = new Label
            {
                Text = item.Line.ProductName,
                FontFamily = "PoppinsSemiBold",
                FontSize = 15,
                TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"],
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 2
            };
            var subtitle = new Label
            {
                Text = $"Picklist {item.Line.OrderedQty:0.##} {item.Line.UnitOfMeasure}",
                Style = (Style)Application.Current.Resources["MetaLabel"]
            };
            Button? editButton = null;
            if (lineIsLocked)
            {
                editButton = BuildActionButton("Edit", IconGlyphs.ClipboardList);
                editButton.HeightRequest = 34;
                editButton.MinimumHeightRequest = 34;
                editButton.FontSize = 11;
                editButton.Padding = new Thickness(10, 4);
            }

            var header = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new(GridLength.Auto),
                    new(GridLength.Star),
                    new(GridLength.Auto)
                },
                ColumnSpacing = 12
            };
            header.Add(new Border
            {
                WidthRequest = 48,
                HeightRequest = 48,
                StrokeThickness = 0,
                BackgroundColor = (Color)Application.Current.Resources["CardTint"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                Content = new Label
                {
                    Text = IconGlyphs.BoxesStacked,
                    Style = (Style)Application.Current.Resources["IconLabel"],
                    FontSize = 16,
                    TextColor = (Color)Application.Current.Resources["Primary"],
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                }
            }, 0);
            header.Add(new VerticalStackLayout { Spacing = 2, VerticalOptions = LayoutOptions.Center, Children = { title, subtitle } }, 1);
            if (editButton is not null)
            {
                header.Add(editButton, 2);
            }

            var qtyValueStack = new VerticalStackLayout
            {
                Spacing = 0,
                Children =
                {
                    new Label
                    {
                        Text = "Loaded Qty",
                        FontFamily = "PoppinsSemiBold",
                        FontSize = 10,
                        TextColor = (Color)Application.Current.Resources["TextSecondaryLight"],
                        HorizontalOptions = LayoutOptions.Center
                    },
                    qtyEntry
                }
            };

            var quantityGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new(GridLength.Auto),
                    new(GridLength.Star),
                    new(GridLength.Auto)
                },
                ColumnSpacing = 10
            };
            quantityGrid.Add(qtyMinus, 0);
            quantityGrid.Add(qtyValueStack, 1);
            quantityGrid.Add(qtyPlus, 2);

            var quantityDock = new Border
            {
                Stroke = (Color)Application.Current.Resources["CardBorderLight"],
                StrokeThickness = 1,
                BackgroundColor = (Color)Application.Current.Resources["SurfaceLight"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                Padding = new Thickness(10, 8),
                Content = quantityGrid
            };

            // Picklist Qty (read-only, what Office actually released for this SKU) sits right
            // beside the editable Loaded Qty, so a supervisor can see both at a glance while
            // adjusting - without them, a short load is only visible after tapping Short below.
            var picklistQtyDock = new Border
            {
                Stroke = (Color)Application.Current.Resources["CardBorderLight"],
                StrokeThickness = 1,
                BackgroundColor = (Color)Application.Current.Resources["CardTint"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                Padding = new Thickness(10, 8),
                Content = new VerticalStackLayout
                {
                    Spacing = 0,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Children =
                    {
                        new Label
                        {
                            Text = "Picklist Qty",
                            FontFamily = "PoppinsSemiBold",
                            FontSize = 10,
                            TextColor = (Color)Application.Current.Resources["TextSecondaryLight"],
                            HorizontalOptions = LayoutOptions.Center
                        },
                        new Label
                        {
                            Text = item.Line.OrderedQty.ToString("0.##", CultureInfo.InvariantCulture),
                            FontFamily = "PoppinsBold",
                            FontSize = 16,
                            TextColor = (Color)Application.Current.Resources["TextPrimaryLight"],
                            HorizontalOptions = LayoutOptions.Center
                        }
                    }
                }
            };

            var quantitiesRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new(GridLength.Star),
                    new(new GridLength(1.4, GridUnitType.Star))
                },
                ColumnSpacing = 10
            };
            quantitiesRow.Add(picklistQtyDock, 0);
            quantitiesRow.Add(quantityDock, 1);

            var fields = new Grid
            {
                RowDefinitions = new RowDefinitionCollection
                {
                    new(GridLength.Auto)
                }
            };
            fields.Add(BuildInlineField("Notes", notesEntry, IconGlyphs.ClipboardList), 0);

            var cardContent = new VerticalStackLayout { Spacing = 14, Children = { header, quantitiesRow } };

            // Loaded/Short is confirmed ONCE for the whole SKU here, not per zone - internally
            // it still updates every underlying 3D-placement group (see ConfirmLineLoadedAsync/
            // ConfirmLineShortAsync), but the supervisor only ever taps one set of controls.
            var lineActionsHost = new VerticalStackLayout { Spacing = 0 };
            if (!readOnly && !lineIsLocked && lineSteps is { Count: > 0 })
            {
                lineActionsHost.Children.Add(BuildLineActionsSection(item.Line, lineSteps, qtyEntry, notesEntry));
            }
            cardContent.Children.Add(lineActionsHost);

            // Read-only per-zone breakdown - usually just one zone, but a large quantity can be
            // split across multiple sections, each shown as its own status row.
            var groupHost = new VerticalStackLayout { Spacing = 0 };
            if (lineSteps is { Count: > 0 })
            {
                groupHost.Children.Add(BuildGroupsSection(lineSteps));
                cardContent.Children.Add(groupHost);
            }

            if (editButton is not null)
            {
                editButton.Clicked += (_, _) =>
                {
                    qtyEntry.IsEnabled = true;
                    notesEntry.IsEnabled = true;
                    qtyMinus.IsEnabled = true;
                    qtyPlus.IsEnabled = true;
                    lineActionsHost.Children.Clear();
                    if (lineSteps is { Count: > 0 })
                    {
                        lineActionsHost.Children.Add(BuildLineActionsSection(item.Line, lineSteps, qtyEntry, notesEntry));
                    }
                    editButton.IsVisible = false;
                };
            }

            cardContent.Children.Add(fields);

            cardRail.Children.Add(new Border
            {
                Stroke = (Color)Application.Current.Resources["CardBorderLight"],
                StrokeThickness = 1,
                BackgroundColor = (Color)Application.Current.Resources["CardLight"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 22 },
                Padding = new Thickness(14),
                WidthRequest = 344,
                Content = cardContent,
                Shadow = new Shadow
                {
                    Brush = new SolidColorBrush(Color.FromArgb("#DCEAE8")),
                    Offset = new Point(0, 8),
                    Radius = 18,
                    Opacity = 0.28f
                }
            });

            _loadLineRows.Add(new LoadLineRow
            {
                Line = item.Line,
                QtyEntry = qtyEntry,
                NotesEntry = notesEntry
            });
        }

        LoadLinesContainer.Children.Add(new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = cardRail
        });
    }

    // One read-only status row per underlying 3D-placement group (zone) for this line, always
    // stacked vertically - every zone is directly visible without swiping. A line placed into
    // a single zone gets one row; a line split across several sections gets one row each.
    // Loaded/Short is confirmed once for the whole SKU (see BuildLineActionsSection), not here.
    private View BuildGroupsSection(List<LoadConfirmationStep> lineSteps)
    {
        var stack = new VerticalStackLayout { Spacing = 10 };
        foreach (var step in lineSteps)
        {
            stack.Children.Add(BuildGroupStatusRow(step));
        }
        return stack;
    }

    // Confirms Loaded/Short once for the whole SKU line, not per zone - internally still has
    // to update each underlying 3D-placement group (that's what the server tracks), but the
    // supervisor only ever sees and taps one set of controls per SKU.
    private View BuildLineActionsSection(DispatchOrderLine line, List<LoadConfirmationStep> lineSteps, Entry loadedQtyEntry, Entry notesEntry)
    {
        var totalPlanned = (int)line.OrderedQty;

        // Single-select shortfall reason - picking one copies its text straight into the
        // product-level Notes field below (still freely editable afterwards).
        var reasonCaption = new Label
        {
            Text = "Reason (for Short)",
            FontSize = 11,
            FontFamily = "PoppinsSemiBold",
            TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"]
        };

        var mutedBorder = (Color)Application.Current.Resources["CardBorderLight"];
        var mutedText = (Color)Application.Current.Resources["TextSecondaryLight"];
        var reasonActiveColor = (Color)Application.Current.Resources["Primary"];
        var reasonChips = new List<(Border Border, Label Label, string Reason)>();
        string? selectedReason = ShortReasons.FirstOrDefault(r => r == notesEntry.Text);

        void RestyleReasonChips()
        {
            foreach (var (chipBorder, chipLabel, reason) in reasonChips)
            {
                var selected = reason == selectedReason;
                chipBorder.Stroke = selected ? reasonActiveColor : mutedBorder;
                chipBorder.StrokeThickness = selected ? 2 : 1.5;
                chipLabel.TextColor = selected ? reasonActiveColor : mutedText;
                chipLabel.FontFamily = selected ? "PoppinsBold" : "PoppinsSemiBold";
            }
        }

        var reasonsRow = new HorizontalStackLayout { Spacing = 8 };
        foreach (var reason in ShortReasons)
        {
            var chipLabel = new Label { Text = reason, FontSize = 11, FontFamily = "PoppinsSemiBold" };
            var chipBorder = new Border
            {
                StrokeThickness = 1.5,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
                BackgroundColor = (Color)Application.Current.Resources["CardLight"],
                Padding = new Thickness(10, 6),
                Content = chipLabel
            };
            chipBorder.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    selectedReason = reason;
                    notesEntry.Text = reason;
                    RestyleReasonChips();
                })
            });
            reasonChips.Add((chipBorder, chipLabel, reason));
            reasonsRow.Children.Add(chipBorder);
        }
        RestyleReasonChips();
        var reasonsScroller = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = reasonsRow
        };

        var loadedButton = BuildActionButton("Loaded", IconGlyphs.CircleCheck, true);
        var shortButton = BuildActionButton("Short", IconGlyphs.Minus);

        var shortWarningLabel = new Label
        {
            Text = $"Short must be less than the picklist {totalPlanned} cartons.",
            FontSize = 11,
            TextColor = (Color)Application.Current.Resources["StatusException"],
            IsVisible = false
        };

        // Realtime validation - Short only makes sense for a quantity strictly less than the
        // whole SKU's picklist quantity; matches the server-side per-group rule (a >= planned
        // quantity is a rejected Short, it should just be Loaded instead), surfaced here before
        // the tap instead of after.
        void UpdateShortValidity()
        {
            var qty = TryParseCartonsInt(loadedQtyEntry.Text);
            var tooHigh = qty is not null && qty >= totalPlanned;
            shortButton.IsEnabled = qty is not null && !tooHigh;
            shortWarningLabel.IsVisible = tooHigh;
        }
        loadedQtyEntry.TextChanged += (_, _) => UpdateShortValidity();
        UpdateShortValidity();

        var actionsRow = new FlexLayout
        {
            Wrap = Microsoft.Maui.Layouts.FlexWrap.Wrap,
            JustifyContent = Microsoft.Maui.Layouts.FlexJustify.Start,
            AlignItems = Microsoft.Maui.Layouts.FlexAlignItems.Center,
            Children = { loadedButton, shortButton }
        };
        foreach (var child in actionsRow.Children)
        {
            ((View)child).Margin = new Thickness(0, 0, 10, 8);
        }

        loadedButton.Clicked += async (_, _) => await ConfirmLineLoadedAsync(lineSteps);

        shortButton.Clicked += async (_, _) =>
        {
            var qty = TryParseCartonsInt(loadedQtyEntry.Text);
            if (qty is null || qty >= totalPlanned)
            {
                await DisplayAlert("Invalid quantity", $"Enter a quantity less than the picklist {totalPlanned} cartons.", "OK");
                return;
            }

            await ConfirmLineShortAsync(lineSteps, qty.Value, string.IsNullOrWhiteSpace(notesEntry.Text) ? null : notesEntry.Text.Trim());
        };

        return new VerticalStackLayout
        {
            Spacing = 10,
            Children = { reasonCaption, reasonsScroller, actionsRow, shortWarningLabel }
        };
    }

    // Loaded (whole SKU): every zone under this line is confirmed Loaded at its own planned
    // quantity - same "mark it all as planned" semantics as the header's "Start Loading" button,
    // just scoped to one SKU.
    private async Task ConfirmLineLoadedAsync(List<LoadConfirmationStep> lineSteps)
    {
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            foreach (var step in lineSteps.OrderBy(s => s.StepNumber))
            {
                await ApiClient.MarkLoadPlanGroupLoadedAsync(_jobId, step.GroupId, null);
            }
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not update", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    // Short (whole SKU): the supervisor only enters ONE total loaded quantity for the SKU, but
    // the server still tracks Loaded/Short per zone - so the total is allocated across this
    // line's zones in placement order (the order they'd actually be loaded off the pick list):
    // each zone is filled to its own planned quantity from what's left, until the total runs
    // out; every zone at or after that point is a Short (the last one partially, any beyond it
    // at zero - nothing was loaded there).
    private async Task ConfirmLineShortAsync(List<LoadConfirmationStep> lineSteps, int totalLoadedQty, string? notes)
    {
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            var remaining = totalLoadedQty;
            foreach (var step in lineSteps.OrderBy(s => s.StepNumber))
            {
                if (remaining >= step.PlannedQuantity)
                {
                    await ApiClient.MarkLoadPlanGroupLoadedAsync(_jobId, step.GroupId, null);
                    remaining -= step.PlannedQuantity;
                }
                else
                {
                    await ApiClient.MarkLoadPlanGroupShortLoadAsync(_jobId, step.GroupId, Math.Max(0, remaining), notes);
                    remaining = 0;
                }
            }
            await LoadAsync();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not update", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }

    // Read-only status for one underlying 3D-placement group (zone) - Loaded/Short is no
    // longer confirmed per zone, only per SKU (see BuildLineActionsSection), so this just
    // reports where each zone currently stands.
    private Border BuildGroupStatusRow(LoadConfirmationStep step)
    {
        var statusColor = StatusColor(step.ConfirmationStatus);
        var statusTint = StatusTint(step.ConfirmationStatus);

        var zoneLabel = new Label
        {
            Text = $"Zone {step.ZoneCode} · {step.PlannedQuantity} cartons",
            FontFamily = "PoppinsSemiBold",
            FontSize = 12,
            TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"],
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var statusBadge = new Border
        {
            Padding = new Thickness(9, 4),
            StrokeThickness = 0,
            BackgroundColor = statusTint,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 11 },
            VerticalOptions = LayoutOptions.Center,
            Content = new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    new Label
                    {
                        Text = StatusIcon(step.ConfirmationStatus),
                        Style = (Style)Application.Current.Resources["IconLabel"],
                        FontSize = 8,
                        TextColor = statusColor,
                        VerticalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = StatusDisplayText(step.ConfirmationStatus),
                        FontFamily = "PoppinsBold",
                        FontSize = 10,
                        TextColor = statusColor,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };

        var headerRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 10
        };
        headerRow.Add(zoneLabel, 0);
        headerRow.Add(statusBadge, 1);

        var body = new VerticalStackLayout { Spacing = 10, Children = { headerRow } };

        var metaParts = new List<string>();
        if (step.ActualQuantity is not null && step.ActualQuantity != step.PlannedQuantity)
        {
            metaParts.Add($"actual {step.ActualQuantity}");
        }
        if (step.PhotoEvidenceIds.Count > 0)
        {
            metaParts.Add(step.PhotoEvidenceIds.Count == 1 ? "1 photo" : $"{step.PhotoEvidenceIds.Count} photos");
        }
        if (!string.IsNullOrWhiteSpace(step.ActualNotes))
        {
            metaParts.Add(step.ActualNotes);
        }
        if (metaParts.Count > 0)
        {
            body.Children.Add(new Label
            {
                Text = string.Join(" · ", metaParts),
                Style = (Style)Application.Current!.Resources["MetaLabel"],
                FontSize = 11
            });
        }

        return new Border
        {
            Stroke = (Color)Application.Current.Resources["CardBorderLight"],
            StrokeThickness = 1,
            BackgroundColor = (Color)Application.Current.Resources["SurfaceLight"],
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(12, 10),
            Content = body
        };
    }

    private static bool IsResolvedStep(LoadConfirmationStep step) =>
        step.ConfirmationStatus is "Loaded" or "Mismatch" or "ShortLoad" or "Skipped";

    private static decimal ResolvedQuantity(LoadConfirmationStep step) =>
        step.ConfirmationStatus == "Skipped" ? 0 : step.ActualQuantity ?? step.PlannedQuantity;

    private static Button BuildTinyQtyButton(string text, bool enabled) => new()
    {
        Text = text,
        FontFamily = text == "+" ? "PoppinsBold" : "FaSolid",
        FontSize = 11,
        WidthRequest = 34,
        HeightRequest = 34,
        MinimumWidthRequest = 34,
        MinimumHeightRequest = 34,
        Padding = new Thickness(0),
        CornerRadius = 12,
        IsEnabled = enabled,
        Style = (Style)Application.Current!.Resources["ChipButton"],
        BackgroundColor = (Color)Application.Current.Resources["CardLight"],
        BorderColor = (Color)Application.Current.Resources["CardBorderLight"],
        TextColor = (Color)Application.Current.Resources["Primary"]
    };

    private static void AdjustQty(Entry entry, decimal delta)
    {
        if (!decimal.TryParse(entry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) &&
            !decimal.TryParse(entry.Text, out qty))
        {
            qty = 0;
        }

        qty = Math.Max(0, qty + delta);
        entry.Text = qty.ToString(CultureInfo.InvariantCulture);
    }

    private void RenderPhotos(OutwardJob job)
    {
        NoPhotosLabel.IsVisible = job.Photos.Count == 0;
        PhotoGallery.IsVisible = job.Photos.Count > 0;
        PhotoGallery.ItemsSource = BuildPhotoDisplayItems(job);
        _ = DownloadMissingPhotosAsync(job);
    }

    private List<PhotoDisplayItem> BuildPhotoDisplayItems(OutwardJob job) =>
        job.Photos.Select(photo => new PhotoDisplayItem
        {
            Id = photo.Id,
            Type = photo.Type,
            CapturedAt = photo.CapturedAt,
            LocalPath = _localPhotoPaths.TryGetValue(photo.Id, out var localPath) ? localPath : null
        }).ToList();

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
                // Keep the placeholder visible if the server copy is temporarily unavailable.
            }
        }

        if (anyDownloaded && ReferenceEquals(_job, job))
        {
            PhotoGallery.ItemsSource = BuildPhotoDisplayItems(job);
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

    private Border BuildStepCard(LoadConfirmationStep step)
    {
        var statusColor = StatusColor(step.ConfirmationStatus);
        var statusTint = StatusTint(step.ConfirmationStatus);

        var seqBadge = new Border
        {
            WidthRequest = 46,
            HeightRequest = 46,
            StrokeThickness = 0,
            BackgroundColor = statusTint,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            VerticalOptions = LayoutOptions.Start,
            Content = new Label
            {
                Text = step.StepNumber.ToString(),
                TextColor = statusColor,
                FontFamily = "PoppinsBold",
                FontSize = 14,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var metaParts = new List<string> { $"Zone {step.ZoneCode}" };
        if (step.ActualQuantity is not null && step.ActualQuantity != step.PlannedQuantity)
        {
            metaParts.Add($"actual {step.ActualQuantity}");
        }
        if (step.PhotoEvidenceIds.Count > 0)
        {
            metaParts.Add(step.PhotoEvidenceIds.Count == 1 ? "1 photo" : $"{step.PhotoEvidenceIds.Count} photos");
        }
        if (!string.IsNullOrWhiteSpace(step.ActualNotes))
        {
            metaParts.Add(step.ActualNotes);
        }

        var titleStack = new VerticalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = $"{step.ProductName} - {step.PlannedQuantity} cartons",
                    FontFamily = "PoppinsSemiBold",
                    FontSize = 14,
                    TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"],
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 2
                },
                new Label
                {
                    Text = string.Join(" · ", metaParts),
                    Style = (Style)Application.Current.Resources["MetaLabel"],
                    LineBreakMode = LineBreakMode.TailTruncation,
                    MaxLines = 1
                }
            }
        };

        var statusBadge = new Border
        {
            Padding = new Thickness(10, 5),
            StrokeThickness = 0,
            BackgroundColor = statusTint,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            VerticalOptions = LayoutOptions.Start,
            HorizontalOptions = LayoutOptions.End,
            Content = new HorizontalStackLayout
            {
                Spacing = 5,
                Children =
                {
                    new Label
                    {
                        Text = StatusIcon(step.ConfirmationStatus),
                        Style = (Style)Application.Current.Resources["IconLabel"],
                        FontSize = 9,
                        TextColor = statusColor,
                        VerticalOptions = LayoutOptions.Center
                    },
                    new Label
                    {
                        Text = StatusDisplayText(step.ConfirmationStatus),
                        FontFamily = "PoppinsBold",
                        FontSize = 11,
                        TextColor = statusColor,
                        VerticalOptions = LayoutOptions.Center
                    }
                }
            }
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star)
            },
            RowDefinitions = new RowDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Auto)
            },
            ColumnSpacing = 12,
            RowSpacing = 14
        };
        headerGrid.Add(seqBadge, 0);
        headerGrid.Add(titleStack, 1);
        headerGrid.Add(statusBadge, 1, 1);

        // Purely a status tile now - the one-tap "confirm all" lives on the header card above,
        // and per-group overrides (actual qty, Mismatch/Short/Skip, Photo, notes) live in the
        // matching group row under "Load lines" below.
        var card = new VerticalStackLayout { Spacing = 14, Children = { headerGrid } };

        return new Border
        {
            Stroke = (Color)Application.Current.Resources["CardBorderLight"],
            StrokeThickness = 1,
            BackgroundColor = (Color)Application.Current.Resources["CardLight"],
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 20 },
            Padding = new Thickness(16, 14),
            WidthRequest = 316,
            MinimumHeightRequest = 126,
            Content = card,
            Shadow = new Shadow
            {
                Brush = new SolidColorBrush(Color.FromArgb("#DCEAE8")),
                Offset = new Point(0, 8),
                Radius = 18,
                Opacity = 0.35f
            }
        };
    }

    private static Button BuildActionButton(string text, string icon, bool primary = false)
    {
        var color = primary
            ? (Color)Application.Current!.Resources["PrimaryDarkText"]
            : (Color)Application.Current!.Resources["Primary"];

        var button = new Button
        {
            Text = text,
            Style = (Style)Application.Current!.Resources[primary ? "PrimaryActionButton" : "ChipButton"],
            HeightRequest = 38,
            MinimumHeightRequest = 38,
            MinimumWidthRequest = 64,
            Padding = new Thickness(10, 5),
            FontSize = 12,
            CornerRadius = 13,
            ImageSource = new FontImageSource
            {
                FontFamily = "FaSolid",
                Glyph = icon,
                Size = 12,
                Color = color
            }
        };

        if (!primary)
        {
            button.BackgroundColor = (Color)Application.Current.Resources["CardLight"];
            button.BorderColor = (Color)Application.Current.Resources["CardBorderLight"];
        }

        return button;
    }

    private static Grid BuildInlineField(string caption, Entry entry, string icon)
    {
        var grid = new Grid();
        grid.Add(new Border
        {
            Stroke = (Color)Application.Current!.Resources["CardBorderLight"],
            StrokeThickness = 1,
            BackgroundColor = (Color)Application.Current.Resources["CardLight"],
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(12, 8),
            Content = new VerticalStackLayout
            {
                Spacing = 2,
                Children =
                {
                    new HorizontalStackLayout
                    {
                        Spacing = 6,
                        Children =
                        {
                            new Label
                            {
                                Text = icon,
                                Style = (Style)Application.Current.Resources["IconLabel"],
                                FontSize = 10,
                                TextColor = (Color)Application.Current.Resources["TextSecondaryLight"],
                                VerticalOptions = LayoutOptions.Center
                            },
                            new Label
                            {
                                Text = caption,
                                FontSize = 11,
                                FontFamily = "PoppinsSemiBold",
                                TextColor = (Color)Application.Current.Resources["TextSecondaryLight"],
                                LineBreakMode = LineBreakMode.TailTruncation,
                                VerticalOptions = LayoutOptions.Center
                            }
                        }
                    },
                    entry
                }
            }
        });
        return grid;
    }

    private static Color StatusColor(string status) => status switch
    {
        "Loaded" => (Color)Application.Current!.Resources["StatusSuccess"],
        "Mismatch" or "ShortLoad" => (Color)Application.Current!.Resources["StatusException"],
        "Started" => (Color)Application.Current!.Resources["StatusAvailable"],
        "Skipped" => (Color)Application.Current!.Resources["StatusNeutral"],
        _ => (Color)Application.Current!.Resources["Primary"]
    };

    private static Color StatusTint(string status) => status switch
    {
        "Loaded" => (Color)Application.Current!.Resources["StatusSuccessTint"],
        "Mismatch" or "ShortLoad" => (Color)Application.Current!.Resources["StatusExceptionTint"],
        "Started" => (Color)Application.Current!.Resources["StatusAvailableTint"],
        _ => (Color)Application.Current!.Resources["CardTint"]
    };

    private static string StatusDisplayText(string status) => status switch
    {
        "NotStarted" => "Not started",
        "Started" => "In progress",
        "Loaded" => "Loaded",
        "Mismatch" => "Mismatch",
        "ShortLoad" => "Short load",
        "Skipped" => "Skipped",
        _ => status
    };

    private static string StatusIcon(string status) => status switch
    {
        "Loaded" => IconGlyphs.CircleCheck,
        "Mismatch" => IconGlyphs.TriangleExclamation,
        "ShortLoad" => IconGlyphs.Minus,
        "Skipped" => IconGlyphs.CircleXmark,
        "Started" => IconGlyphs.Clock,
        _ => IconGlyphs.Clock
    };

    private static int? TryParseInt(string? text) => int.TryParse(text, out var value) ? value : null;

    private static int? TryParseCartonsInt(string? text)
    {
        if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue) ||
            int.TryParse(text, out intValue))
        {
            return intValue;
        }

        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var decimalValue) ||
            decimal.TryParse(text, out decimalValue))
        {
            return (int)Math.Round(decimalValue, MidpointRounding.AwayFromZero);
        }

        return null;
    }

    // ---------- Photo evidence (job-level, single generic capture, capped at MaxPhotosPerJob) ----------

    private const int MaxPhotosPerJob = 5;

    private async void OnCapturePhotoClicked(object? sender, EventArgs e) => await CaptureJobPhotoAsync();

    private async Task CaptureJobPhotoAsync()
    {
        if ((_job?.Photos.Count ?? 0) >= MaxPhotosPerJob)
        {
            await DisplayAlert("Limit reached", $"You can add up to {MaxPhotosPerJob} photos per job.", "OK");
            return;
        }

        try
        {
            if (!MediaPicker.Default.IsCaptureSupported)
            {
                await DisplayAlert("Not supported", "This device does not support photo capture.", "OK");
                return;
            }

            var localPath = await PhotoCapture.CaptureAndSaveAsync();
            if (localPath is null)
            {
                return;
            }

            Spinner.IsVisible = true;
            Spinner.IsRunning = true;
            _job = await ApiClient.UploadOutwardPhotoAsync(_jobId, "VehicleLoaded", localPath);
            var newest = _job.Photos.OrderBy(p => p.Id).LastOrDefault();
            if (newest is not null)
            {
                _localPhotoPaths[newest.Id] = localPath;
            }
            RenderFinalSection(_lastSteps);
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
        }
    }

    private async Task<bool> SubmitCurrentLoadLinesAsync()
    {
        var lines = new List<LoadLineInput>();
        for (var i = 0; i < _loadLineRows.Count; i++)
        {
            var row = _loadLineRows[i];
            if (!decimal.TryParse(row.QtyEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) &&
                !decimal.TryParse(row.QtyEntry.Text, out qty))
            {
                await DisplayAlert("Missing quantity", $"Enter loaded quantity for {row.Line.ProductName}.", "OK");
                return false;
            }

            lines.Add(new LoadLineInput
            {
                DispatchOrderLineId = row.Line.Id,
                LoadedQty = qty,
                LoadSequence = i + 1,
                Notes = string.IsNullOrWhiteSpace(row.NotesEntry.Text) ? null : row.NotesEntry.Text.Trim()
            });
        }

        try
        {
            _job = await ApiClient.SubmitLoadLinesAsync(_jobId, lines);
            return true;
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not record load lines", ex.Message, "OK");
            return false;
        }
    }

    // ---------- Dispatch ready + Complete ----------

    private async void OnConfirmDispatchReadyClicked(object? sender, EventArgs e)
    {
        ConfirmDispatchReadyButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.ConfirmDispatchReadyAsync(_jobId);
            RenderFinalSection(_lastSteps);
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
            if (!await SubmitCurrentLoadLinesAsync())
            {
                CompleteButton.IsEnabled = true;
                return;
            }

            await ApiClient.CompleteOutwardAsync(_jobId);
            await DisplayAlert("Loading complete", "This job has been marked complete.", "OK");
            await Shell.Current.GoToAsync("//SupervisorTabs/SupervisorHomePage");
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not complete", ex.Message, "OK");
            CompleteButton.IsEnabled = true;
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
        }
    }
}
