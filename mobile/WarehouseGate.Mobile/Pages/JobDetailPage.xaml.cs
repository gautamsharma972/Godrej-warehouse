using System.Globalization;
using WarehouseGate.Mobile.Converters;
using WarehouseGate.Mobile.Models;
using WarehouseGate.Mobile.Services;

namespace WarehouseGate.Mobile.Pages;

[QueryProperty(nameof(JobId), "id")]
public partial class JobDetailPage : ContentPage
{
    private static readonly string[] StepLabels = { "Gate", "Assign", "Dock", "Inspect", "Done" };
    private static readonly string[] Conditions = { "Ok", "Damaged", "Short", "Excess", "Mismatch" };
    private static readonly StatusToColorConverter ColorConverter = new();
    private static readonly StatusToDisplayTextConverter TextConverter = new();

    private int _jobId;
    private InwardJob? _job;
    private readonly List<InspectionRow> _inspectionRows = new();
    private readonly List<UnplannedRow> _unplannedRows = new();
    private readonly Dictionary<int, string> _localPhotoPaths = new();
    private readonly Dictionary<int, string> _localDocumentPaths = new();
    private string? _photoFilterType;
    private bool _outwardReferenceFetched;
    private string? _lastOutwardReferenceVizPayload;
    private InwardOutwardReference? _lastOutwardReference;
    private bool _baysFetched;
    private string? _selectedBayName;
    private bool _isLoadingJob;

    // OnAppearing fires again the moment a modal WE pushed (SkuPickerPage, PhotoViewerPage) pops
    // back to this page - its unconditional LoadAsync() would otherwise reload from the server and
    // wipe any not-yet-saved Mismatch SKU Details rows the user just added. Set true right before
    // pushing a modal that doesn't itself refresh _job; OnAppearing consumes and resets it once.
    private bool _suppressNextAppearingReload;

    private class InspectionRow
    {
        public required PoLine Line { get; init; }
        public required Entry NotesEntry { get; init; }
        public required Dictionary<string, Entry> QuantityEntries { get; init; }
        public required List<ConditionBox> ConditionBoxes { get; init; }
    }

    private class ConditionBox
    {
        public required string Condition { get; init; }
        public required Border Background { get; init; }
        public required Label Label { get; init; }
        public required Entry QuantityEntry { get; init; }
    }

    private class UnplannedRow
    {
        public required Border Container { get; init; }
        public required Label SkuLabel { get; init; }
        public required Entry QuantityEntry { get; init; }
        public int? ProductId { get; set; }
        public string? ProductName { get; set; }
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

    public JobDetailPage()
    {
        InitializeComponent();
    }

    private void OnPhotoSectionHeaderTapped(object? sender, TappedEventArgs e)
    {
        PhotoSectionContent.IsVisible = !PhotoSectionContent.IsVisible;
        PhotoSectionChevron.Text = PhotoSectionContent.IsVisible
            ? IconGlyphs.ChevronUp
            : IconGlyphs.ChevronDown;
    }

    private void OnInspectionSectionHeaderTapped(object? sender, TappedEventArgs e)
    {
        InspectionSectionContent.IsVisible = !InspectionSectionContent.IsVisible;
        InspectionSectionChevron.Text = InspectionSectionContent.IsVisible
            ? IconGlyphs.ChevronUp
            : IconGlyphs.ChevronDown;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        SupervisorHubClient.JobUpdated += OnHubJobUpdated;

        if (_suppressNextAppearingReload)
        {
            _suppressNextAppearingReload = false;
            return;
        }

        _ = LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SupervisorHubClient.JobUpdated -= OnHubJobUpdated;
    }

    private void OnHubJobUpdated(InwardJob job)
    {
        if (job.Id != _jobId)
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Adding a photo (CapturePhotoAsync) itself triggers this exact push right back at the
            // caller - the server broadcasts "JobUpdated" to the whole Supervisors group the moment
            // AddPhotoAsync saves, including this device's own connection. Without the same
            // snapshot/restore guard used there, this handler would silently undo that fix a moment
            // later: CapturePhotoAsync's own RenderJob()+restore completes first, then this hub push
            // arrives and wipes the restored Ok/Damaged/Short/Excess/Mismatch/notes right back out.
            var snapshot = SnapshotInspectionEntries();
            _job = job;
            RenderJob();
            RestoreInspectionEntries(snapshot);
        });
    }

    private async Task LoadAsync()
    {
        if (_isLoadingJob)
        {
            return;
        }

        _isLoadingJob = true;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.GetJobAsync(_jobId).WaitAsync(TimeSpan.FromSeconds(20));
            RenderJob();
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
            _isLoadingJob = false;
        }
    }

    private static int StatusIndex(string status) => status switch
    {
        "GateIn" => 0,
        "Assigned" => 1,
        "Docked" => 2,
        "Inspecting" => 3,
        "Completed" => 4,
        _ => 0
    };

    private void RenderJob()
    {
        var job = _job!;
        PageHeaderView.PageTitle = "Inbound job";
        PageHeaderView.Title = job.VehicleNumber;
        HeaderLabel.Text = job.InwardTxnNumber;
        SubHeaderLabel.Text = $"PO {job.PONumber} - {job.SupplierName}";

        var statusColor = (Color)ColorConverter.Convert(job.Status, typeof(Color), null, CultureInfo.CurrentCulture);
        StatusBadgeBorder.BackgroundColor = statusColor;
        StatusLabel.Text = (string)TextConverter.Convert(job.Status, typeof(string), null, CultureInfo.CurrentCulture);

        DockInSection.IsVisible = job.Status == "Assigned" && !Session.IsSecurity;
        StartUnloadingSection.IsVisible = job.Status == "Docked" && !Session.IsSecurity;

        if (DockInSection.IsVisible && !_baysFetched)
        {
            _baysFetched = true;
            _ = LoadBayChipsAsync();
        }

        TimeTrackingLabel.IsVisible = job.HasTimeTrackingCaption;
        TimeTrackingLabel.Text = job.TimeTrackingCaption;

        // PendingOfficeVerification (unloading done, Office hasn't confirmed yet) shows the same
        // Photos/Inspection detail as Inspecting/Completed, just entirely read-only - Supervisor can
        // still review what they submitted, they just can't change it once it's with Office.
        var showPhotosAndInspection = job.Status is "Inspecting" or "PendingOfficeVerification" or "Completed";
        PhotoSection.IsVisible = showPhotosAndInspection;
        InspectionSection.IsVisible = showPhotosAndInspection;

        if (showPhotosAndInspection && !_outwardReferenceFetched)
        {
            _outwardReferenceFetched = true;
            _ = LoadOutwardReferenceAsync();
        }

        var readOnly = job.Status is "PendingOfficeVerification" or "Completed" || Session.IsSecurity;
        CaptureVehiclePhotoButton.IsEnabled = !readOnly && showPhotosAndInspection;
        CaptureMaterialPhotoButton.IsEnabled = !readOnly && showPhotosAndInspection;
        CaptureExceptionPhotoButton.IsEnabled = !readOnly && showPhotosAndInspection;

        RenderPhotos(job);
        RenderDocuments(job);
        RestylePhotoFilterButtons();
        BuildInspectionRows(job, readOnly);
        BuildUnplannedRows(job, readOnly);

        SubmitInspectionButton.IsVisible = !readOnly;
        InspectionFooterGrid.IsVisible = !readOnly;
        AddUnplannedLineButton.IsVisible = !readOnly;
        CompleteButton.IsVisible = job.Status == "Inspecting" && !Session.IsSecurity;
        CompleteButton.IsEnabled = job.Photos.Count > 0;
        CompleteHintLabel.IsVisible = job.Status == "Inspecting" && job.Photos.Count == 0 && !Session.IsSecurity;
        CompleteHintLabel.Text = "Add at least one photo before completing.";

        if (job.Grn is not null)
        {
            GrnBorder.IsVisible = true;
            var exception = job.Grn.HasExceptions;
            var accent = exception
                ? (Color)Application.Current!.Resources["StatusException"]
                : (Color)Application.Current!.Resources["StatusSuccess"];
            var tint = exception
                ? Color.FromArgb("#FDECEF")
                : (Color)Application.Current.Resources["StatusSuccessTint"];
            GrnBorder.BackgroundColor = tint;
            GrnIconBadge.BackgroundColor = Colors.White;
            GrnIconLabel.TextColor = accent;
            GrnIconLabel.Text = exception ? IconGlyphs.TriangleExclamation : IconGlyphs.ClipboardCheck;
            GrnTitleLabel.TextColor = accent;
            GrnTitleLabel.Text = exception ? "Goods Receipt Note - exceptions flagged" : "Goods Receipt Note";
            GrnLabel.Text = exception
                ? $"{job.Grn.GrnNumber}, generated {job.Grn.GeneratedAt.ToLocalTime():g} - needs supplier follow-up"
                : $"{job.Grn.GrnNumber}, generated {job.Grn.GeneratedAt.ToLocalTime():g}";
        }

        // A rendered job means the current fetch/update completed successfully. Keep a stale
        // fire-and-forget refresh from leaving the shared page loader spinning over valid data.
        Spinner.IsRunning = false;
        Spinner.IsVisible = false;
    }

    // Read-only cross-reference: how this same shipment was actually loaded at the origin
    // warehouse, when it went through our own Outward flow. Fetched once per page visit - it's
    // historical data that never changes once set, so there's no need to refetch on every hub push.
    // Deliberately a catch-all (not just ApiException/network failures): this section is purely
    // additive, and any failure inside it - JSON, resource lookup, the HybridWebView not being
    // ready yet, anything - must never be allowed to escape a fire-and-forget call and take down
    // the rest of this page (or the app) instead of just quietly staying hidden.
    private async Task LoadOutwardReferenceAsync()
    {
        try
        {
            var reference = await ApiClient.GetOutwardReferenceAsync(_jobId);
            if (!reference.Exists)
            {
                // Show the section WITH an explanation rather than hiding it - a silently
                // missing section reads as a bug to supervisors expecting the 3D view, when
                // the truth is simply that this shipment never went through the origin
                // warehouse's Outward 3D planning flow (e.g. gate-checked-in directly).
                OutwardReferenceSubtitleLabel.Text = "This shipment arrived without an Outward 3D load plan from the origin warehouse.";
                OutwardReferenceEmptyBorder.IsVisible = true;
                OutwardReferenceContentGrid.IsVisible = false;
                OutwardReferenceSection.IsVisible = true;
                return;
            }

            OutwardReferenceEmptyBorder.IsVisible = false;
            OutwardReferenceContentGrid.IsVisible = true;
            OutwardReferenceSubtitleLabel.Text =
                $"Dispatched as {reference.DispatchOrderNumber} to {reference.CustomerName} - vehicle {reference.VehicleNumber}.";

            OutwardReferenceSequenceContainer.Children.Clear();
            foreach (var group in reference.Groups.OrderBy(g => g.LoadSequence))
            {
                OutwardReferenceSequenceContainer.Children.Add(BuildOutwardReferenceSequenceRow(group));
            }

            _lastOutwardReference = reference;

            OutwardReferenceSection.IsVisible = true;
        }
        catch
        {
            OutwardReferenceSection.IsVisible = false;
        }
    }

    private async void OnToggleReferenceSimulationClicked(object? sender, EventArgs e)
    {
        if (_lastOutwardReference is null)
        {
            return;
        }

        SendOutwardReferenceToViewer(_lastOutwardReference);
        LoadSimulationSession.Payload = _lastOutwardReferenceVizPayload;
        LoadSimulationSession.Title = _lastOutwardReference.DispatchOrderNumber ?? "Dispatch load plan";
        _suppressNextAppearingReload = true;
        await Shell.Current.GoToAsync(nameof(LoadSimulationPage));
    }

    private void OnPhotosTabTapped(object? sender, TappedEventArgs e) => ShowEvidenceTab(showPhotos: true);

    private void OnDocumentsTabTapped(object? sender, TappedEventArgs e) => ShowEvidenceTab(showPhotos: false);

    private void ShowEvidenceTab(bool showPhotos)
    {
        PhotosContentGrid.IsVisible = showPhotos;
        DocumentsContentGrid.IsVisible = !showPhotos;

        var primary = (Color)Application.Current!.Resources["Primary"];
        PhotosTabButton.BackgroundColor = showPhotos ? primary : Colors.Transparent;
        DocumentsTabButton.BackgroundColor = showPhotos ? Colors.Transparent : primary;
        PhotosTabLabel.TextColor = showPhotos ? Colors.White : (Color)Application.Current.Resources["TextSecondaryLight"];
        DocumentsTabLabel.TextColor = showPhotos ? (Color)Application.Current.Resources["TextSecondaryLight"] : Colors.White;
    }

    private static Border BuildOutwardReferenceSequenceRow(LoadPlanGroup group)
    {
        var activeColor = (Color)Application.Current!.Resources["Primary"];
        var badge = new Border
        {
            WidthRequest = 28,
            HeightRequest = 28,
            StrokeThickness = 0,
            BackgroundColor = Color.TryParse(group.Color, out var parsedColor) ? parsedColor : activeColor,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
            Content = new Label
            {
                Text = group.LoadSequence.ToString(),
                TextColor = Colors.White,
                FontFamily = "PoppinsBold",
                FontSize = 12,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children =
            {
                new Label
                {
                    Text = group.ProductName,
                    FontFamily = "PoppinsSemiBold",
                    FontSize = 13,
                    LineBreakMode = LineBreakMode.TailTruncation
                },
                new Label
                {
                    Text = $"Zone {group.ZoneCode} - {group.ActualQuantity ?? group.Quantity} cartons",
                    Style = (Style)Application.Current.Resources["MetaLabel"],
                    FontSize = 11
                }
            }
        };

        return new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.White,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(12, 10),
            Content = new HorizontalStackLayout { Spacing = 10, Children = { badge, textStack } }
        };
    }

    // Builds the same 3D-viewer payload used by LoadSimulationPage - the inline card only shows a
    // flat "Loading sequence" list (see BuildOutwardReferenceSequenceRow), so this is purely staged
    // here for the "View 3D" button to hand off via LoadSimulationSession.
    private void SendOutwardReferenceToViewer(InwardOutwardReference reference)
    {
        var visibleGroups = reference.Groups
            .Where(g => (g.ActualQuantity ?? g.Quantity) > 0)
            .OrderBy(g => g.LoadSequence)
            .Select(g => new
            {
                groupId = g.Id,
                name = g.ProductName,
                qty = g.ActualQuantity ?? g.Quantity,
                color = g.Color,
                locked = true,
                code = "",
                location = "",
                x = g.PositionX,
                y = g.PositionY,
                z = g.PositionZ,
                w = g.DimX,
                h = g.DimY,
                d = g.DimZ,
                rows = g.Rows,
                cols = g.Columns,
                layers = g.Layers
            })
            .ToList();

        var payload = new
        {
            vehicle = new
            {
                widthCm = reference.VehicleWidthCm ?? 0,
                lengthCm = reference.VehicleLengthCm ?? 0,
                heightCm = reference.VehicleHeightCm ?? 0,
                number = reference.VehicleNumber ?? "",
                typeLabel = reference.VehicleMaxWeightKg is null ? "" : $"~{Math.Round(reference.VehicleMaxWeightKg.Value / 1000)} Ton Truck"
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

        _lastOutwardReferenceVizPayload = System.Text.Json.JsonSerializer.Serialize(payload);
    }

    private void RenderPhotos(InwardJob job)
    {
        var photos = BuildPhotoDisplayItems(job);
        PhotosTabLabel.Text = $"Photos ({job.Photos.Count})";
        NoPhotosLabel.IsVisible = photos.Count == 0;
        PhotoCarousel.IsVisible = photos.Count > 0;
        PhotoCarouselIndicator.IsVisible = false;
        PhotoCarousel.ItemsSource = photos;
        _ = DownloadMissingPhotosAsync(job);
    }

    private List<PhotoDisplayItem> BuildPhotoDisplayItems(InwardJob job)
    {
        var photos = string.IsNullOrWhiteSpace(_photoFilterType)
            ? job.Photos
            : job.Photos.Where(photo => PhotoMatchesFilter(photo.Type, _photoFilterType));

        return photos.Select(photo => new PhotoDisplayItem
        {
            Id = photo.Id,
            Type = photo.Type,
            CapturedAt = photo.CapturedAt,
            LocalPath = _localPhotoPaths.TryGetValue(photo.Id, out var localPath) ? localPath : null
        }).ToList();
    }

    private static bool PhotoMatchesFilter(string type, string filter) => filter switch
    {
        "Vehicle" => type.StartsWith("Vehicle", StringComparison.OrdinalIgnoreCase),
        "Material" => type.StartsWith("Material", StringComparison.OrdinalIgnoreCase),
        "Exception" => type.Equals("ExceptionProof", StringComparison.OrdinalIgnoreCase),
        _ => true
    };

    // Anything not captured in this app session (a past session's own photo, or one captured by
    // a different device/user) has no local path yet - fetch it once, cache it, and treat it
    // exactly like a local capture from then on. Placeholder-first render above isn't blocked on
    // this; the carousel just refreshes in place once a download lands.
    private async Task DownloadMissingPhotosAsync(InwardJob job)
    {
        var missing = job.Photos.Where(p => !_localPhotoPaths.ContainsKey(p.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var anyDownloaded = false;
        foreach (var photo in missing)
        {
            var cachedPath = Path.Combine(FileSystem.CacheDirectory, $"inward-photo-{photo.Id}{Path.GetExtension(photo.FilePath)}");
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
            PhotoCarousel.IsVisible = photos.Count > 0;
            PhotoCarousel.ItemsSource = photos;
        }
    }

    // Documents (E-Way Bill/Invoice/Lorry Receipt/Other) are captured the same way as gate photos
    // by Security at Gate Check-in - shown here read-only so the supervisor doesn't have to go
    // hunting for them elsewhere. Shares the PhotoDisplayItem/PhotoViewerPage plumbing above since
    // an InwardDocument is just another captured image with a different Type vocabulary.
    private void RenderDocuments(InwardJob job)
    {
        var documents = BuildDocumentDisplayItems(job);
        NoDocumentsLabel.IsVisible = documents.Count == 0;
        DocumentsCarousel.IsVisible = documents.Count > 0;
        DocumentsCarousel.ItemsSource = documents;
        DocumentsTabLabel.Text = $"Documents ({documents.Count})";
        _ = DownloadMissingDocumentsAsync(job);
    }

    private List<PhotoDisplayItem> BuildDocumentDisplayItems(InwardJob job) =>
        job.Documents.Select(document => new PhotoDisplayItem
        {
            Id = document.Id,
            Type = document.Type,
            CapturedAt = document.UploadedAt,
            LocalPath = _localDocumentPaths.TryGetValue(document.Id, out var localPath) ? localPath : null
        }).ToList();

    private async Task DownloadMissingDocumentsAsync(InwardJob job)
    {
        var missing = job.Documents.Where(d => !_localDocumentPaths.ContainsKey(d.Id)).ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var anyDownloaded = false;
        foreach (var document in missing)
        {
            var cachedPath = Path.Combine(FileSystem.CacheDirectory, $"inward-document-{document.Id}{Path.GetExtension(document.FilePath)}");
            if (File.Exists(cachedPath))
            {
                _localDocumentPaths[document.Id] = cachedPath;
                anyDownloaded = true;
                continue;
            }

            try
            {
                var bytes = await ApiClient.DownloadFileAsync(document.FilePath);
                await File.WriteAllBytesAsync(cachedPath, bytes);
                _localDocumentPaths[document.Id] = cachedPath;
                anyDownloaded = true;
            }
            catch
            {
                // Leave it as a placeholder - server copy genuinely unavailable right now.
            }
        }

        if (anyDownloaded && ReferenceEquals(_job, job))
        {
            var documents = BuildDocumentDisplayItems(job);
            NoDocumentsLabel.IsVisible = documents.Count == 0;
            DocumentsCarousel.IsVisible = documents.Count > 0;
            DocumentsCarousel.ItemsSource = documents;
        }
    }

    private async void OnPhotoTapped(object? sender, EventArgs e)
    {
        if (sender is not Border { BindingContext: PhotoDisplayItem item })
        {
            return;
        }

        _suppressNextAppearingReload = true;
        await Navigation.PushModalAsync(new PhotoViewerPage(item));
    }

    private void BuildInspectionRows(InwardJob job, bool readOnly)
    {
        InspectionLinesContainer.Children.Clear();
        _inspectionRows.Clear();
        InspectionCountLabel.Text = job.Lines.Count == 1 ? "1 line" : $"{job.Lines.Count} lines";

        static string FormatQty(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

        for (var lineIndex = 0; lineIndex < job.Lines.Count; lineIndex++)
        {
            var line = job.Lines[lineIndex];
            var existingByCondition = job.InspectionLines
                .Where(l => l.PurchaseOrderLineId == line.Id)
                .GroupBy(l => l.Condition)
                .ToDictionary(g => g.Key, g => g.Sum(x => x.ReceivedQty));
            var enteredQty = existingByCondition.Values.Sum();
            var hasExceptions = existingByCondition.Any(kvp => kvp.Key != "Ok" && kvp.Value > 0);
            var statusText = existingByCondition.Count == 0
                ? "Pending"
                : hasExceptions ? "Review" : "Ok";
            var statusColor = hasExceptions
                ? (Color)Application.Current!.Resources["StatusException"]
                : (Color)ColorConverter.Convert(statusText == "Pending" ? "Assigned" : "Ok", typeof(Color), null, CultureInfo.CurrentCulture);

            var titleColumn = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = line.ProductName,
                        FontFamily = "PoppinsSemiBold",
                        FontSize = 14,
                        TextColor = (Color)Application.Current.Resources["TextPrimaryLight"],
                        LineBreakMode = LineBreakMode.WordWrap,
                        MaxLines = 2
                    },
                    new Label
                    {
                        Text = $"Expected {FormatQty(line.ExpectedQty)} {line.UnitOfMeasure}" + (line.IsExtra ? " · Added during loading" : ""),
                        Style = (Style)Application.Current.Resources["MetaLabel"],
                        FontSize = 12
                    }
                }
            };

            var statusBadgeLabel = new Label
            {
                Text = statusText,
                FontSize = 10,
                FontFamily = "PoppinsBold",
                TextColor = statusColor,
                VerticalOptions = LayoutOptions.Center
            };
            var statusBadge = new Border
            {
                Padding = new Thickness(12, 6),
                StrokeThickness = 0,
                BackgroundColor = (Color)Application.Current.Resources["CardTint"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                HorizontalOptions = LayoutOptions.End,
                VerticalOptions = LayoutOptions.Center,
                Content = statusBadgeLabel
            };

            var enteredLabel = new Label
            {
                Text = $"Entered {FormatQty(enteredQty)}",
                FontSize = 11,
                FontFamily = "PoppinsSemiBold",
                TextColor = (Color)Application.Current.Resources["Primary"],
                VerticalOptions = LayoutOptions.Center
            };
            var enteredBadge = new Border
            {
                Padding = new Thickness(0),
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                VerticalOptions = LayoutOptions.Center,
                Content = enteredLabel
            };
            var chevronLabel = new Label
            {
                Text = IconGlyphs.ChevronDown,
                FontFamily = "FaSolid",
                FontSize = 12,
                TextColor = (Color)Application.Current.Resources["TextSecondaryLight"],
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Center
            };
            var chevronBadge = new Border
            {
                WidthRequest = 36,
                HeightRequest = 36,
                StrokeThickness = 0,
                BackgroundColor = (Color)Application.Current.Resources["SurfaceLight"],
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 14 },
                VerticalOptions = LayoutOptions.Center,
                Content = chevronLabel
            };

            var headerActions = new HorizontalStackLayout
            {
                Spacing = 10,
                HorizontalOptions = LayoutOptions.Start,
                VerticalOptions = LayoutOptions.Center,
                Children = { enteredBadge, statusBadge }
            };
            statusBadge.Padding = new Thickness(0);
            statusBadge.BackgroundColor = Colors.Transparent;

            var titleRow = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection
                {
                    new(GridLength.Star),
                    new(GridLength.Auto)
                },
                RowDefinitions = new RowDefinitionCollection
                {
                    new(GridLength.Auto),
                    new(GridLength.Auto)
                },
                ColumnSpacing = 10,
                RowSpacing = 5
            };
            Grid.SetColumn(titleColumn, 0);
            Grid.SetColumn(chevronBadge, 1);
            Grid.SetRowSpan(chevronBadge, 2);
            Grid.SetRow(headerActions, 1);
            Grid.SetColumn(headerActions, 0);
            titleRow.Children.Add(titleColumn);
            titleRow.Children.Add(headerActions);
            titleRow.Children.Add(chevronBadge);

            var notesEntry = new Entry
            {
                Placeholder = "Notes for damaged, short, excess, or mismatch quantities",
                Text = job.InspectionLines.FirstOrDefault(l => l.PurchaseOrderLineId == line.Id && !string.IsNullOrWhiteSpace(l.Notes))?.Notes ?? string.Empty,
                IsEnabled = !readOnly,
                HeightRequest = 54
            };

            var row = new InspectionRow
            {
                Line = line,
                NotesEntry = notesEntry,
                QuantityEntries = new Dictionary<string, Entry>(),
                ConditionBoxes = new List<ConditionBox>(),
            };

            void RefreshEnteredTotal()
            {
                var total = row.QuantityEntries.Values
                    .Where(entry => TryParseQuantity(entry.Text, out var parsed) && parsed > 0)
                    .Sum(entry => TryParseQuantity(entry.Text, out var parsed) ? parsed : 0);
                enteredLabel.Text = $"Entered {FormatQty(total)}";
            }

            // Damaged/Short/Mismatch cartons were never actually received as good units of this SKU,
            // so they come OUT of Ok rather than being entered as extra on top of it - otherwise the
            // total entered quantity balloons past what was really delivered (Expected 997 + 5 Damaged
            // reading as 1002 received, instead of 992 Ok + 5 Damaged = 997). Excess is the one
            // exception: it's genuinely more than Expected, so it stays purely additive.
            void RecomputeOkFromExceptions()
            {
                if (!row.QuantityEntries.TryGetValue("Ok", out var okEntry))
                {
                    return;
                }

                var deducted = new[] { "Damaged", "Short", "Mismatch" }
                    .Sum(c => row.QuantityEntries.TryGetValue(c, out var entry) && TryParseQuantity(entry.Text, out var qty) ? qty : 0);

                var recomputedOk = Math.Max(0, line.ExpectedQty - deducted);
                // "0" format, not the default ToString - a decimal like 790.00m stringifies with its
                // stored scale ("790.00"), and the digits-only TextChanged filter below strips the "."
                // without removing the zeros, silently turning it into "79000".
                var recomputedText = recomputedOk.ToString("0", CultureInfo.InvariantCulture);
                if (okEntry.Text != recomputedText)
                {
                    okEntry.Text = recomputedText;
                }
            }

            // A single-column form is calmer and more predictable than a dashboard of quantity
            // tiles. Each condition gets one full-width, thumb-friendly row.
            var exceptionsGrid = new VerticalStackLayout
            {
                Spacing = 8
            };

            View BuildConditionCard(string condition)
            {
                var color = (Color)ColorConverter.Convert(condition, typeof(Color), null, CultureInfo.CurrentCulture);
                var existingQty = existingByCondition.TryGetValue(condition, out var savedQty)
                    ? savedQty.ToString("0", CultureInfo.InvariantCulture)
                    : condition == "Ok" && existingByCondition.Count == 0
                    ? line.ExpectedQty.ToString("0", CultureInfo.InvariantCulture)
                        : string.Empty;

                var label = new Label
                {
                    Text = condition,
                    FontSize = 12,
                    FontFamily = "PoppinsSemiBold",
                    TextColor = (Color)Application.Current.Resources["TextSecondaryLight"],
                    VerticalOptions = LayoutOptions.Center,
                    LineBreakMode = LineBreakMode.NoWrap,
                    MaxLines = 1
                };

                var qtyInput = new Entry
                {
                    Placeholder = "0",
                    Keyboard = Keyboard.Numeric,
                    Text = existingQty,
                    IsEnabled = !readOnly,
                    HorizontalTextAlignment = TextAlignment.Center,
                    FontFamily = "PoppinsBold",
                    FontSize = 14,
                    WidthRequest = 64,
                    HeightRequest = 40,
                    TextColor = color
                };

                var contentGrid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new(GridLength.Auto),
                        new(GridLength.Star),
                        new(GridLength.Auto)
                    },
                    RowDefinitions = new RowDefinitionCollection
                    {
                        new(GridLength.Star),
                        new(new GridLength(1))
                    },
                    ColumnSpacing = 9
                };
                var marker = new Label
                {
                    Text = IconGlyphs.CircleCheck,
                    FontFamily = "FaSolid",
                    FontSize = 13,
                    TextColor = color,
                    VerticalOptions = LayoutOptions.Center
                };
                Grid.SetColumn(marker, 0);
                Grid.SetColumn(label, 1);
                Grid.SetColumn(qtyInput, 2);
                contentGrid.Children.Add(marker);
                contentGrid.Children.Add(label);
                contentGrid.Children.Add(qtyInput);
                var divider = new BoxView
                {
                    Color = Color.FromArgb("#E8EFEE"),
                    HeightRequest = 1,
                    VerticalOptions = LayoutOptions.End
                };
                Grid.SetRow(divider, 1);
                Grid.SetColumn(divider, 0);
                Grid.SetColumnSpan(divider, 3);
                contentGrid.Children.Add(divider);

                var card = new Border
                {
                    StrokeThickness = 0,
                    BackgroundColor = Colors.Transparent,
                    Padding = new Thickness(4, 2),
                    HeightRequest = 46,
                    HorizontalOptions = LayoutOptions.Fill,
                    Content = contentGrid
                };

                row.QuantityEntries[condition] = qtyInput;
                row.ConditionBoxes.Add(new ConditionBox
                {
                    Condition = condition,
                    Background = card,
                    Label = label,
                    QuantityEntry = qtyInput
                });

                if (!readOnly)
                {
                    qtyInput.TextChanged += (_, e) =>
                    {
                        // Digits-only, mirrors the Bay Number field's filter elsewhere on this page.
                        var digitsOnly = new string((e.NewTextValue ?? string.Empty).Where(char.IsDigit).ToArray());
                        if (qtyInput.Text != digitsOnly)
                        {
                            qtyInput.Text = digitsOnly;
                            return;
                        }

                        if (condition is "Damaged" or "Short" or "Mismatch")
                        {
                            RecomputeOkFromExceptions();
                        }
                        RestyleConditionBoxes(row);
                        RefreshEnteredTotal();
                    };
                }

                return card;
            }

            for (var i = 0; i < Conditions.Length; i++)
            {
                var conditionCard = BuildConditionCard(Conditions[i]);
                exceptionsGrid.Children.Add(conditionCard);
            }

            RestyleConditionBoxes(row);
            RefreshEnteredTotal();

            var notesBorder = new Border
            {
                Stroke = (Color)Application.Current.Resources["CardBorderLight"],
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
                BackgroundColor = Color.FromArgb("#F8FAFC"),
                Padding = new Thickness(12, 0),
                Content = notesEntry
            };

            var detailsStack = new VerticalStackLayout
            {
                Spacing = 10,
                IsVisible = false,
                Children =
                {
                    new VerticalStackLayout
                    {
                        Spacing = 2,
                        Children =
                        {
                            new Label
                            {
                                Text = "Received quantities",
                                FontFamily = "PoppinsSemiBold",
                                FontSize = 14,
                                TextColor = (Color)Application.Current.Resources["TextPrimaryLight"]
                            }
                        }
                    },
                    exceptionsGrid,
                    BuildSkuPhotoRow(line, readOnly),
                    notesBorder
                }
            };

            var detailsDivider = new BoxView
            {
                HeightRequest = 1,
                Color = Color.FromArgb("#E8EDF3"),
                IsVisible = false
            };

            var headerTap = new TapGestureRecognizer();
            headerTap.Tapped += (_, _) =>
            {
                detailsStack.IsVisible = !detailsStack.IsVisible;
                detailsDivider.IsVisible = detailsStack.IsVisible;
                chevronLabel.Text = detailsStack.IsVisible ? IconGlyphs.ChevronUp : IconGlyphs.ChevronDown;
            };
            titleRow.GestureRecognizers.Add(headerTap);

            var card = new VerticalStackLayout
            {
                Spacing = 12,
                Children =
                {
                    titleRow,
                    detailsDivider,
                    detailsStack
                }
            };
            InspectionLinesContainer.Children.Add(new Border
            {
                Stroke = (Color)Application.Current.Resources["CardBorderLight"],
                StrokeThickness = 1,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 16 },
                BackgroundColor = Colors.White,
                Padding = new Thickness(14),
                Shadow = new Shadow
                {
                    Brush = new SolidColorBrush(Color.FromArgb("#120F3475")),
                    Offset = new Point(0, 2),
                    Radius = 8,
                    Opacity = 0.22f
                },
                Content = card
            });

            _inspectionRows.Add(row);
        }
    }

    private const int MaxPhotosPerSkuLine = 2;

    private View BuildSkuPhotoRow(PoLine line, bool readOnly)
    {
        var count = _job!.Photos.Count(p => p.Type == "SkuCondition" && p.PurchaseOrderLineId == line.Id);

        var countLabel = new Label
        {
            Text = $"{count}/{MaxPhotosPerSkuLine} photos",
            FontFamily = "PoppinsSemiBold",
            FontSize = 12,
            TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"],
            VerticalOptions = LayoutOptions.Center
        };

        var addButton = new Button
        {
            Text = "Add Photo",
            IsEnabled = !readOnly && count < MaxPhotosPerSkuLine,
            Style = (Style)Application.Current!.Resources["ChipButton"],
            FontSize = 11,
            ImageSource = new FontImageSource { FontFamily = "FaSolid", Glyph = IconGlyphs.Camera, Color = (Color)Application.Current.Resources["Primary"], Size = 11 }
        };
        addButton.Clicked += async (_, _) => await CapturePhotoAsync("SkuCondition", line.Id);

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(GridLength.Auto), new(GridLength.Star), new(GridLength.Auto) },
            ColumnSpacing = 10
        };
        var iconLabel = new Label
        {
            Text = IconGlyphs.Camera,
            FontFamily = "FaSolid",
            FontSize = 12,
            TextColor = (Color)Application.Current.Resources["TextSecondaryLight"],
            VerticalOptions = LayoutOptions.Center
        };
        Grid.SetColumn(iconLabel, 0);
        Grid.SetColumn(countLabel, 1);
        Grid.SetColumn(addButton, 2);
        grid.Children.Add(iconLabel);
        grid.Children.Add(countLabel);
        grid.Children.Add(addButton);
        return grid;
    }

    private void BuildUnplannedRows(InwardJob job, bool readOnly)
    {
        UnplannedLinesContainer.Children.Clear();
        _unplannedRows.Clear();

        foreach (var line in job.UnplannedLines)
        {
            var row = AddUnplannedRow(readOnly);
            row.ProductId = line.ProductId;
            row.ProductName = line.ProductName;
            row.SkuLabel.Text = string.IsNullOrWhiteSpace(line.SkuCode) ? line.ProductName : $"{line.ProductName} ({line.SkuCode})";
            row.SkuLabel.TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"];
            // Whole-number text only - QuantityEntry's own TextChanged handler strips any
            // non-digit character (including a decimal point), so formatting with "0.##" or
            // similar here would get mangled (e.g. "12.00" -> "1200") the instant this assignment
            // fires it.
            row.QuantityEntry.Text = line.Quantity.ToString("0", CultureInfo.InvariantCulture);
        }
    }

    private async void OnAddUnplannedLineClicked(object? sender, EventArgs e)
    {
        var row = AddUnplannedRow(readOnly: false);

        var tcs = new TaskCompletionSource<SkuMasterItem?>();
        _suppressNextAppearingReload = true;
        await Navigation.PushModalAsync(new SkuPickerPage(result => tcs.TrySetResult(result)));
        var selected = await tcs.Task;
        if (selected is null)
        {
            RemoveUnplannedRow(row);
            return;
        }

        row.ProductId = selected.Id;
        row.ProductName = selected.Name;
        row.SkuLabel.Text = string.IsNullOrWhiteSpace(selected.SkuCode) ? selected.Name : $"{selected.Name} ({selected.SkuCode})";
        row.SkuLabel.TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"];
    }

    private UnplannedRow AddUnplannedRow(bool readOnly)
    {
        var skuLabel = new Label
        {
            Text = "Select SKU",
            FontFamily = "PoppinsSemiBold",
            FontSize = 13,
            TextColor = (Color)Application.Current!.Resources["TextSecondaryLight"],
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        var qtyEntry = new Entry
        {
            Placeholder = "Qty",
            Keyboard = Keyboard.Numeric,
            IsEnabled = !readOnly,
            HorizontalTextAlignment = TextAlignment.Center,
            WidthRequest = 70,
            FontFamily = "PoppinsBold"
        };
        qtyEntry.TextChanged += (_, e) =>
        {
            var digitsOnly = new string((e.NewTextValue ?? string.Empty).Where(char.IsDigit).ToArray());
            if (qtyEntry.Text != digitsOnly)
            {
                qtyEntry.Text = digitsOnly;
            }
        };

        var removeButton = new Button
        {
            Text = IconGlyphs.Xmark,
            IsVisible = !readOnly,
            Style = (Style)Application.Current!.Resources["ChipButton"],
            FontFamily = "FaSolid",
            FontSize = 11,
            WidthRequest = 38,
            HeightRequest = 38,
            Padding = 0
        };

        var skuIcon = new Border
        {
            WidthRequest = 34,
            HeightRequest = 34,
            StrokeThickness = 0,
            BackgroundColor = (Color)Application.Current.Resources["CardTint"],
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 10 },
            Content = new Label
            {
                Text = IconGlyphs.BoxesStacked,
                FontFamily = "FaSolid",
                FontSize = 12,
                TextColor = (Color)Application.Current.Resources["Primary"],
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new(GridLength.Auto),
                new(GridLength.Star),
                new(GridLength.Auto),
                new(GridLength.Auto)
            },
            ColumnSpacing = 10
        };
        Grid.SetColumn(skuIcon, 0);
        Grid.SetColumn(skuLabel, 1);
        Grid.SetColumn(qtyEntry, 2);
        Grid.SetColumn(removeButton, 3);
        grid.Children.Add(skuIcon);
        grid.Children.Add(skuLabel);
        grid.Children.Add(qtyEntry);
        grid.Children.Add(removeButton);

        var container = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.White,
            StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(10, 8),
            Content = grid
        };

        skuLabel.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                if (readOnly)
                {
                    return;
                }

                var row = _unplannedRows.First(r => r.Container == container);
                var tcs = new TaskCompletionSource<SkuMasterItem?>();
                _suppressNextAppearingReload = true;
                await Navigation.PushModalAsync(new SkuPickerPage(result => tcs.TrySetResult(result)));
                var selected = await tcs.Task;
                if (selected is null)
                {
                    return;
                }

                row.ProductId = selected.Id;
                row.ProductName = selected.Name;
                skuLabel.Text = string.IsNullOrWhiteSpace(selected.SkuCode) ? selected.Name : $"{selected.Name} ({selected.SkuCode})";
                skuLabel.TextColor = (Color)Application.Current!.Resources["TextPrimaryLight"];
            })
        });

        var newRow = new UnplannedRow { Container = container, SkuLabel = skuLabel, QuantityEntry = qtyEntry };
        removeButton.Clicked += (_, _) => RemoveUnplannedRow(newRow);

        _unplannedRows.Add(newRow);
        UnplannedLinesContainer.Children.Add(container);
        return newRow;
    }

    private void RemoveUnplannedRow(UnplannedRow row)
    {
        _unplannedRows.Remove(row);
        UnplannedLinesContainer.Children.Remove(row.Container);
    }

    private static void RestyleConditionBoxes(InspectionRow row)
    {
        var mutedText = (Color)Application.Current.Resources["TextSecondaryLight"];

        foreach (var box in row.ConditionBoxes)
        {
            var hasQty = decimal.TryParse(box.QuantityEntry.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out var qty) && qty > 0;
            var activeColor = (Color)ColorConverter.Convert(box.Condition, typeof(Color), null, CultureInfo.CurrentCulture);

            box.Background.StrokeThickness = 0;
            box.Background.BackgroundColor = Colors.Transparent;
            box.Label.TextColor = hasQty ? activeColor : mutedText;
            box.Label.FontFamily = hasQty ? "PoppinsBold" : "PoppinsSemiBold";
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
        string bayName;
        if (BayChipsScroll.IsVisible)
        {
            if (string.IsNullOrWhiteSpace(_selectedBayName))
            {
                await DisplayAlert("Bay required", "Select a bay before docking in.", "OK");
                return;
            }
            bayName = _selectedBayName;
        }
        else
        {
            if (!int.TryParse(BayNumberEntry.Text, out var bayNumber) || bayNumber < 1 || bayNumber > 50)
            {
                await DisplayAlert("Bay required", "Enter a bay number between 1 and 50 before docking in.", "OK");
                return;
            }
            bayName = $"Bay-{bayNumber}";
        }

        DockInButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.DockInAsync(_jobId, bayName);
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

    private async void OnStartUnloadingClicked(object? sender, EventArgs e)
    {
        StartUnloadingButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.StartUnloadingAsync(_jobId);
            RenderJob();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not start", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            StartUnloadingButton.IsEnabled = true;
        }
    }

    private async void OnCaptureVehiclePhotoClicked(object? sender, EventArgs e) =>
        await HandleEvidenceButtonAsync("Vehicle", "VehicleBefore");

    private async void OnCaptureMaterialPhotoClicked(object? sender, EventArgs e) =>
        await HandleEvidenceButtonAsync("Material", "MaterialBefore");

    private async void OnCaptureExceptionPhotoClicked(object? sender, EventArgs e) =>
        await HandleEvidenceButtonAsync("Exception", "ExceptionProof");

    private async Task HandleEvidenceButtonAsync(string filterType, string captureType)
    {
        if (_job?.Status == "Completed")
        {
            TogglePhotoFilter(filterType);
            return;
        }

        await CapturePhotoAsync(captureType);
    }

    private void TogglePhotoFilter(string filterType)
    {
        _photoFilterType = _photoFilterType == filterType ? null : filterType;
        if (_job is not null)
        {
            RenderPhotos(_job);
        }

        RestylePhotoFilterButtons();
    }

    private void RestylePhotoFilterButtons()
    {
        RestylePhotoFilterButton(CaptureVehiclePhotoButton, "Vehicle", IconGlyphs.Truck);
        RestylePhotoFilterButton(CaptureMaterialPhotoButton, "Material", IconGlyphs.BoxesStacked);
        RestylePhotoFilterButton(CaptureExceptionPhotoButton, "Exception", IconGlyphs.TriangleExclamation);
    }

    private void RestylePhotoFilterButton(Button button, string filterType, string glyph)
    {
        var isActive = _job?.Status == "Completed" && _photoFilterType == filterType;
        var primary = (Color)Application.Current!.Resources["Primary"];
        var tint = (Color)Application.Current.Resources["CardTint"];

        button.BackgroundColor = isActive ? primary : tint;
        button.TextColor = isActive ? Colors.White : primary;
        button.FontFamily = isActive ? "PoppinsBold" : "PoppinsSemiBold";
        button.ImageSource = new FontImageSource
        {
            FontFamily = "FaSolid",
            Glyph = glyph,
            Color = isActive ? Colors.White : primary,
            Size = 13
        };
    }

    private async Task CapturePhotoAsync(string type, int? purchaseOrderLineId = null)
    {
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

            CaptureVehiclePhotoButton.IsEnabled = false;
            CaptureMaterialPhotoButton.IsEnabled = false;
            CaptureExceptionPhotoButton.IsEnabled = false;
            Spinner.IsVisible = true;
            Spinner.IsRunning = true;

            _job = await ApiClient.UploadPhotoAsync(_jobId, type, localPath, purchaseOrderLineId);
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
            // RenderJob() rebuilds every inspection row from scratch (BuildInspectionRows) - only
            // the job's Photos actually changed here, but a full rebuild would otherwise silently
            // wipe any Ok/Damaged/Short/Excess/Mismatch quantity or notes the supervisor had typed
            // but not yet submitted. Snapshot before, reapply after (matched by PO line id, which
            // doesn't change across the rebuild).
            var snapshot = SnapshotInspectionEntries();
            RenderJob();
            RestoreInspectionEntries(snapshot);
        }
    }

    private Dictionary<int, (string Notes, Dictionary<string, string> Quantities)> SnapshotInspectionEntries() =>
        _inspectionRows.ToDictionary(
            r => r.Line.Id,
            r => (r.NotesEntry.Text, r.QuantityEntries.ToDictionary(kv => kv.Key, kv => kv.Value.Text)));

    private void RestoreInspectionEntries(Dictionary<int, (string Notes, Dictionary<string, string> Quantities)> snapshot)
    {
        foreach (var row in _inspectionRows)
        {
            if (!snapshot.TryGetValue(row.Line.Id, out var saved))
            {
                continue;
            }

            row.NotesEntry.Text = saved.Notes;
            foreach (var (condition, text) in saved.Quantities)
            {
                if (row.QuantityEntries.TryGetValue(condition, out var entry))
                {
                    entry.Text = text;
                }
            }
        }
    }

    private async void OnSubmitInspectionClicked(object? sender, EventArgs e)
    {
        var lines = new List<InspectionLineInput>();
        foreach (var row in _inspectionRows)
        {
            var hasAnyQuantity = false;
            var notes = string.IsNullOrWhiteSpace(row.NotesEntry.Text) ? null : row.NotesEntry.Text.Trim();

            foreach (var condition in Conditions)
            {
                var entry = row.QuantityEntries[condition];
                if (string.IsNullOrWhiteSpace(entry.Text))
                {
                    continue;
                }

                if (!TryParseQuantity(entry.Text, out var qty))
                {
                    await DisplayAlert("Invalid quantity", $"Enter a valid {condition.ToLowerInvariant()} quantity for {row.Line.ProductName}.", "OK");
                    return;
                }

                if (qty <= 0)
                {
                    continue;
                }

                hasAnyQuantity = true;
                lines.Add(new InspectionLineInput
                {
                    PurchaseOrderLineId = row.Line.Id,
                    ReceivedQty = qty,
                    Condition = condition,
                    Notes = condition == "Ok" ? null : notes
                });
            }

            if (!hasAnyQuantity)
            {
                await DisplayAlert("Missing quantity", $"Enter at least one inspection quantity for {row.Line.ProductName}.", "OK");
                return;
            }
        }

        var unplannedLines = new List<UnplannedReceiptLineInput>();
        foreach (var row in _unplannedRows)
        {
            if (row.ProductId is null)
            {
                await DisplayAlert("Missing SKU", "Select a SKU for every row in Mismatch SKU Details, or remove the row.", "OK");
                return;
            }

            if (!TryParseQuantity(row.QuantityEntry.Text, out var qty) || qty <= 0)
            {
                await DisplayAlert("Invalid quantity", $"Enter a valid mismatch quantity for {row.ProductName}.", "OK");
                return;
            }

            unplannedLines.Add(new UnplannedReceiptLineInput { ProductId = row.ProductId.Value, Quantity = qty });
        }

        SubmitInspectionButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.SubmitInspectionAsync(_jobId, lines, unplannedLines);
            RenderJob();
        }
        catch (ApiException ex)
        {
            await DisplayAlert("Could not save inspection", ex.Message, "OK");
        }
        finally
        {
            Spinner.IsVisible = false;
            Spinner.IsRunning = false;
            SubmitInspectionButton.IsEnabled = true;
        }
    }

    private static bool TryParseQuantity(string text, out decimal quantity) =>
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out quantity) ||
        decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out quantity);

    private async void OnCompleteClicked(object? sender, EventArgs e)
    {
        CompleteButton.IsEnabled = false;
        Spinner.IsVisible = true;
        Spinner.IsRunning = true;
        try
        {
            _job = await ApiClient.CompleteAsync(_jobId);
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
            CompleteButton.IsEnabled = _job?.Photos.Count > 0;
        }
    }
}


