using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using WarehouseGate.Mobile.Models;

namespace WarehouseGate.Mobile.Services;

public class ApiException : Exception
{
    public ApiException(string message) : base(message)
    {
    }
}

public static class ApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly HttpClient Http = new()
    {
        BaseAddress = new Uri(AppConfig.ApiBaseUrl)
    };

    private static HttpRequestMessage NewRequest(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrEmpty(Session.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Session.Token);
        }
        return request;
    }

    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    private static async Task<T> SendAsync<T>(HttpRequestMessage request)
    {
        using var response = await SendWithTimeoutAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var message = TryExtractErrorMessage(body) ?? $"Request failed ({(int)response.StatusCode}).";
            throw new ApiException(message);
        }

        // Streams and parses directly off the response body instead of buffering the whole
        // payload into one string first - for large lists (history, jobs with many groups/lines)
        // that avoids holding two full copies in memory and spreads the parse across async
        // continuations rather than one big synchronous call on the calling (UI) thread.
        return (await response.Content.ReadFromJsonAsync<T>(JsonOptions))!;
    }

    private static async Task SendAsync(HttpRequestMessage request)
    {
        using var response = await SendWithTimeoutAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync();
            var message = TryExtractErrorMessage(body) ?? $"Request failed ({(int)response.StatusCode}).";
            throw new ApiException(message);
        }
    }

    // Single choke point for every request: without this, a stalled connection hangs on the
    // HttpClient's default 100s timeout, leaving a page's spinner running with no way out.
    private static async Task<HttpResponseMessage> SendWithTimeoutAsync(HttpRequestMessage request)
    {
        using var cts = new CancellationTokenSource(RequestTimeout);
        try
        {
            return await Http.SendAsync(request, cts.Token);
        }
        catch (OperationCanceledException)
        {
            throw new ApiException("Request timed out. Check your connection and try again.");
        }
    }

    private static string? TryExtractErrorMessage(string body)
    {
        try
        {
            var error = JsonSerializer.Deserialize<ApiError>(body, JsonOptions);
            return error?.Message;
        }
        catch
        {
            return null;
        }
    }

    public static async Task<LoginResponse> LoginAsync(string userName, string password, string? organizationCode = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login")
        {
            Content = JsonContent.Create(new { userName, password, organizationCode })
        };
        return await SendAsync<LoginResponse>(request);
    }

    public static async Task<InwardJob> GateCheckInAsync(GateCheckInInput input)
    {
        var request = NewRequest(HttpMethod.Post, "api/gate/checkin");
        request.Content = JsonContent.Create(new
        {
            vehicleNumber = input.VehicleNumber,
            inwardTxnNumber = input.InwardTxnNumber,
            poNumber = input.PONumber,
            driverName = input.DriverName,
            driverMobile = input.DriverMobile,
            transporterName = input.TransporterName,
            gateName = input.GateName,
            gpsLatitude = input.GpsLatitude,
            gpsLongitude = input.GpsLongitude,
            remarks = input.Remarks
        });
        return await SendAsync<InwardJob>(request);
    }

    public static async Task<InwardJob> UploadGatePhotoAsync(int id, string type, string filePath)
    {
        var request = NewRequest(HttpMethod.Post, $"api/gate/{id}/photos");

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent
        {
            { new StringContent(type), "type" }
        };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        request.Content = content;
        return await SendAsync<InwardJob>(request);
    }

    public static async Task<InwardJob> UploadGateDocumentAsync(int id, string type, string filePath)
    {
        var request = NewRequest(HttpMethod.Post, $"api/gate/{id}/documents");

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent
        {
            { new StringContent(type), "type" }
        };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        request.Content = content;
        return await SendAsync<InwardJob>(request);
    }

    // 404 here means "not in the vehicle master" - a normal, expected outcome (not every plate is
    // known), so it's returned as null rather than surfaced as an ApiException like every other
    // non-success response.
    public static async Task<VehicleMasterDto?> GetVehicleMasterAsync(string vehicleNumber)
    {
        var request = NewRequest(HttpMethod.Get, $"api/gate/vehicle-master/{Uri.EscapeDataString(vehicleNumber)}");
        using var response = await SendWithTimeoutAsync(request);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            var message = TryExtractErrorMessage(body) ?? $"Request failed ({(int)response.StatusCode}).";
            throw new ApiException(message);
        }

        return JsonSerializer.Deserialize<VehicleMasterDto>(body, JsonOptions);
    }

    public static async Task<List<VehicleMasterDto>> GetVehicleMastersAsync() =>
        await SendAsync<List<VehicleMasterDto>>(NewRequest(HttpMethod.Get, "api/gate/vehicle-masters"));

    // Active dock bays for the caller's own warehouse. Empty list means the warehouse has no bay
    // master defined yet, and callers fall back to legacy free-number bay entry.
    public static async Task<List<string>> GetBaysAsync() =>
        await SendAsync<List<string>>(NewRequest(HttpMethod.Get, "api/bays"));

    // For fetching back a previously-uploaded photo/document (api/files/{relativePath}, the same
    // relative path already stored on the Photo/Document DTO's FilePath).
    public static async Task<byte[]> DownloadFileAsync(string relativePath)
    {
        var request = NewRequest(HttpMethod.Get, $"api/files/{relativePath}");
        using var response = await SendWithTimeoutAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException($"Could not download file ({(int)response.StatusCode}).");
        }

        return await response.Content.ReadAsByteArrayAsync();
    }

    public static async Task<List<InwardJob>> GetSecurityTransactionsAsync(
        bool activeOnly, string? vehicleNumber = null, string? poNumber = null, DateTime? date = null)
    {
        var query = $"activeOnly={activeOnly}";
        if (!string.IsNullOrWhiteSpace(vehicleNumber)) query += $"&vehicleNumber={Uri.EscapeDataString(vehicleNumber)}";
        if (!string.IsNullOrWhiteSpace(poNumber)) query += $"&poNumber={Uri.EscapeDataString(poNumber)}";
        if (date.HasValue) query += $"&date={date.Value:yyyy-MM-dd}";

        return await SendAsync<List<InwardJob>>(NewRequest(HttpMethod.Get, $"api/gate/transactions?{query}"));
    }

    public static async Task<List<InwardJob>> GetPendingExitJobsAsync(string? vehicleNumber = null)
    {
        var query = string.IsNullOrWhiteSpace(vehicleNumber) ? "" : $"?vehicleNumber={Uri.EscapeDataString(vehicleNumber)}";
        return await SendAsync<List<InwardJob>>(NewRequest(HttpMethod.Get, $"api/gate/transactions/pending-exit{query}"));
    }

    public static async Task<List<ExpectedShipment>> GetExpectedShipmentsAsync() =>
        await SendAsync<List<ExpectedShipment>>(NewRequest(HttpMethod.Get, "api/gate/expected-shipments"));

    public static async Task<List<string>> GetTransportersAsync() =>
        await SendAsync<List<string>>(NewRequest(HttpMethod.Get, "api/gate/transporters"));

    public static async Task<InwardJob> RecordExitAsync(int id, string filePath)
    {
        var request = NewRequest(HttpMethod.Post, $"api/gate/{id}/exit");

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        request.Content = content;
        return await SendAsync<InwardJob>(request);
    }

    // No longer requires a matching pick list - Security just logs the physical arrival (mirrors
    // GateCheckInAsync's decoupled Inward check-in). Office links this arrival to a pending pick
    // list afterward from the portal.
    public static async Task<OutwardGateArrival> OutwardGateCheckInAsync(OutwardGateCheckInInput input)
    {
        var request = NewRequest(HttpMethod.Post, "api/outward-gate/checkin");
        request.Content = JsonContent.Create(new
        {
            vehicleNumber = input.VehicleNumber,
            driverName = input.DriverName,
            driverMobile = input.DriverMobile,
            transporterName = input.TransporterName,
            gateName = input.GateName,
            gpsLatitude = input.GpsLatitude,
            gpsLongitude = input.GpsLongitude,
            dispatchOrderNumber = input.DispatchOrderNumber
        });
        return await SendAsync<OutwardGateArrival>(request);
    }

    public static async Task<OutwardGateArrival> UploadOutwardGateArrivalPhotoAsync(int arrivalId, string type, string filePath)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward-gate/{arrivalId}/photos");

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent
        {
            { new StringContent(type), "type" }
        };
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        request.Content = content;
        return await SendAsync<OutwardGateArrival>(request);
    }

    // Real Admin Vehicle Registry, searchable - backs the Outward gate-in picker. Distinct from
    // GetVehicleMastersAsync below (Supervisor's Dock-In picker, unrelated to this gate-in flow).
    public static async Task<List<VehicleRegistryEntry>> GetVehicleRegistryAsync() =>
        await SendAsync<List<VehicleRegistryEntry>>(NewRequest(HttpMethod.Get, "api/gate/vehicle-registry"));

    public static async Task<List<OutwardJob>> GetOutwardSecurityTransactionsAsync(
        bool activeOnly, string? vehicleNumber = null, string? dispatchOrderNumber = null, DateTime? date = null)
    {
        var query = $"activeOnly={activeOnly}";
        if (!string.IsNullOrWhiteSpace(vehicleNumber)) query += $"&vehicleNumber={Uri.EscapeDataString(vehicleNumber)}";
        if (!string.IsNullOrWhiteSpace(dispatchOrderNumber)) query += $"&dispatchOrderNumber={Uri.EscapeDataString(dispatchOrderNumber)}";
        if (date.HasValue) query += $"&date={date.Value:yyyy-MM-dd}";

        return await SendAsync<List<OutwardJob>>(NewRequest(HttpMethod.Get, $"api/outward-gate/transactions?{query}"));
    }

    public static async Task<List<OutwardJob>> GetOutwardPendingExitJobsAsync(string? vehicleNumber = null)
    {
        var query = string.IsNullOrWhiteSpace(vehicleNumber) ? "" : $"?vehicleNumber={Uri.EscapeDataString(vehicleNumber)}";
        return await SendAsync<List<OutwardJob>>(NewRequest(HttpMethod.Get, $"api/outward-gate/transactions/pending-exit{query}"));
    }

    public static async Task<OutwardJob> RecordOutwardExitAsync(int id, string filePath)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward-gate/{id}/exit");

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        request.Content = content;
        return await SendAsync<OutwardJob>(request);
    }

    public static async Task<List<InwardJob>> GetMyJobsAsync() =>
        await SendAsync<List<InwardJob>>(NewRequest(HttpMethod.Get, "api/inward/mine"));

    public static async Task<List<InwardJob>> GetInwardHistoryAsync(
        string? vehicleNumber = null, string? poNumber = null, DateTime? date = null)
    {
        var query = string.Empty;
        if (!string.IsNullOrWhiteSpace(vehicleNumber)) query += $"&vehicleNumber={Uri.EscapeDataString(vehicleNumber)}";
        if (!string.IsNullOrWhiteSpace(poNumber)) query += $"&poNumber={Uri.EscapeDataString(poNumber)}";
        if (date.HasValue) query += $"&date={date.Value:yyyy-MM-dd}";

        return await SendAsync<List<InwardJob>>(NewRequest(HttpMethod.Get, $"api/inward/history?{query.TrimStart('&')}"));
    }

    public static async Task<InwardJob> GetJobAsync(int id) =>
        await SendAsync<InwardJob>(NewRequest(HttpMethod.Get, $"api/inward/{id}"));

    public static async Task<InwardOutwardReference> GetOutwardReferenceAsync(int id) =>
        await SendAsync<InwardOutwardReference>(NewRequest(HttpMethod.Get, $"api/inward/{id}/outward-reference"));

    public static async Task<InwardJob> DockInAsync(int id, string bayName)
    {
        var request = NewRequest(HttpMethod.Post, $"api/inward/{id}/dock-in");
        request.Content = JsonContent.Create(new { bayName });
        return await SendAsync<InwardJob>(request);
    }

    public static async Task<InwardJob> StartUnloadingAsync(int id) =>
        await SendAsync<InwardJob>(NewRequest(HttpMethod.Post, $"api/inward/{id}/start"));

    public static async Task<InwardJob> UploadPhotoAsync(int id, string type, string filePath, int? purchaseOrderLineId = null)
    {
        var request = NewRequest(HttpMethod.Post, $"api/inward/{id}/photos");

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent
        {
            { new StringContent(type), "type" }
        };
        if (purchaseOrderLineId is int lineId)
        {
            content.Add(new StringContent(lineId.ToString()), "purchaseOrderLineId");
        }
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        request.Content = content;
        return await SendAsync<InwardJob>(request);
    }

    public static async Task<InwardJob> SubmitInspectionAsync(int id, List<InspectionLineInput> lines, List<UnplannedReceiptLineInput>? unplannedLines = null)
    {
        var request = NewRequest(HttpMethod.Post, $"api/inward/{id}/inspection");
        request.Content = JsonContent.Create(new { lines, unplannedLines = unplannedLines ?? new List<UnplannedReceiptLineInput>() });
        return await SendAsync<InwardJob>(request);
    }

    public static async Task<List<SkuMasterItem>> SearchSkuMasterAsync(string? search = null)
    {
        var url = "api/inward/sku-master" + (string.IsNullOrWhiteSpace(search) ? "" : $"?search={Uri.EscapeDataString(search)}");
        return await SendAsync<List<SkuMasterItem>>(NewRequest(HttpMethod.Get, url));
    }

    public static async Task<InwardJob> CompleteAsync(int id) =>
        await SendAsync<InwardJob>(NewRequest(HttpMethod.Post, $"api/inward/{id}/complete"));

    public static async Task<List<OutwardJob>> GetMyOutwardJobsAsync() =>
        await SendAsync<List<OutwardJob>>(NewRequest(HttpMethod.Get, "api/outward/mine"));

    public static async Task<List<OutwardJob>> GetOutwardHistoryAsync(
        string? vehicleNumber = null, string? dispatchOrderNumber = null, DateTime? date = null)
    {
        var query = string.Empty;
        if (!string.IsNullOrWhiteSpace(vehicleNumber)) query += $"&vehicleNumber={Uri.EscapeDataString(vehicleNumber)}";
        if (!string.IsNullOrWhiteSpace(dispatchOrderNumber)) query += $"&dispatchOrderNumber={Uri.EscapeDataString(dispatchOrderNumber)}";
        if (date.HasValue) query += $"&date={date.Value:yyyy-MM-dd}";

        return await SendAsync<List<OutwardJob>>(NewRequest(HttpMethod.Get, $"api/outward/history?{query.TrimStart('&')}"));
    }

    public static async Task<OutwardJob> GetOutwardJobAsync(int id) =>
        await SendAsync<OutwardJob>(NewRequest(HttpMethod.Get, $"api/outward/{id}"));

    public static async Task<OutwardJob> DockInOutwardAsync(int id, string vehicleNumber, string bayName)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/dock-in");
        request.Content = JsonContent.Create(new { vehicleNumber, bayName });
        return await SendAsync<OutwardJob>(request);
    }

    public static async Task<OutwardJob> StartLoadingAsync(int id) =>
        await SendAsync<OutwardJob>(NewRequest(HttpMethod.Post, $"api/outward/{id}/start"));

    public static async Task<OutwardJob> UploadOutwardPhotoAsync(int id, string type, string filePath, int? dispatchOrderLineId = null)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/photos");

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent
        {
            { new StringContent(type), "type" }
        };
        if (dispatchOrderLineId is int lineId)
        {
            content.Add(new StringContent(lineId.ToString()), "dispatchOrderLineId");
        }
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        request.Content = content;
        return await SendAsync<OutwardJob>(request);
    }

    public static async Task<OutwardJob> SubmitLoadLinesAsync(int id, List<LoadLineInput> lines)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-lines");
        request.Content = JsonContent.Create(new { lines });
        return await SendAsync<OutwardJob>(request);
    }

    // Adds a SKU beyond the original pick list - lets a Supervisor use up remaining vehicle space
    // discovered while planning the load (see LoadPlanEditorPage's "Add SKU" card).
    public static async Task<OutwardJob> AddOutwardDispatchLineAsync(int id, int productId, decimal quantity)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/lines");
        request.Content = JsonContent.Create(new { productId, quantity });
        return await SendAsync<OutwardJob>(request);
    }

    public static async Task<OutwardJob> ReportOutwardExceptionAsync(int id, string reason, string? remarks)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/exception");
        request.Content = JsonContent.Create(new { reason, remarks });
        return await SendAsync<OutwardJob>(request);
    }

    public static async Task<OutwardJob> ConfirmDispatchReadyAsync(int id) =>
        await SendAsync<OutwardJob>(NewRequest(HttpMethod.Post, $"api/outward/{id}/confirm-dispatch-ready"));

    public static async Task<OutwardJob> CompleteOutwardAsync(int id) =>
        await SendAsync<OutwardJob>(NewRequest(HttpMethod.Post, $"api/outward/{id}/complete"));

    public static async Task<OutwardJob> RestartLoadingAsync(int id) =>
        await SendAsync<OutwardJob>(NewRequest(HttpMethod.Post, $"api/outward/{id}/restart-loading"));

    public static async Task<LoadPlanResult> GetOutwardLoadPlanAsync(int id) =>
        await SendAsync<LoadPlanResult>(NewRequest(HttpMethod.Get, $"api/outward/{id}/load-plan"));

    // ---------- 3D load plan: options ----------

    public static async Task<List<LoadPlanOptionSummary>> GetLoadPlanOptionsAsync(int id) =>
        await SendAsync<List<LoadPlanOptionSummary>>(NewRequest(HttpMethod.Get, $"api/outward/{id}/load-plan/options"));

    public static async Task<LoadPlanOptionSummary> CreateLoadPlanOptionAsync(int id, string? label)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/options");
        request.Content = JsonContent.Create(new { label });
        return await SendAsync<LoadPlanOptionSummary>(request);
    }

    public static async Task DeleteLoadPlanOptionAsync(int id, int optionId) =>
        await SendAsync(NewRequest(HttpMethod.Delete, $"api/outward/{id}/load-plan/options/{optionId}"));

    public static async Task<LoadPlanOptionSummary> SelectLoadPlanOptionAsync(int id, int optionId) =>
        await SendAsync<LoadPlanOptionSummary>(NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/options/{optionId}/select"));

    // ---------- 3D load plan: groups ----------

    public static async Task<List<LoadPlanGroup>> GetLoadPlanGroupsAsync(int id, int optionId) =>
        await SendAsync<List<LoadPlanGroup>>(NewRequest(HttpMethod.Get, $"api/outward/{id}/load-plan/options/{optionId}/groups"));

    public static async Task<LoadGroupPreview> PreviewLoadPlanGroupAsync(int id, int optionId, PlaceLoadGroupInput input)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/options/{optionId}/groups/preview");
        request.Content = JsonContent.Create(ToRequestBody(input));
        return await SendAsync<LoadGroupPreview>(request);
    }

    public static async Task<LoadPlanGroup> CreateLoadPlanGroupAsync(int id, int optionId, PlaceLoadGroupInput input)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/options/{optionId}/groups");
        request.Content = JsonContent.Create(ToRequestBody(input));
        return await SendAsync<LoadPlanGroup>(request);
    }

    public static async Task<LoadPlanGroup> UpdateLoadPlanGroupAsync(int id, int optionId, int groupId, PlaceLoadGroupInput input)
    {
        var request = NewRequest(HttpMethod.Put, $"api/outward/{id}/load-plan/options/{optionId}/groups/{groupId}");
        request.Content = JsonContent.Create(ToRequestBody(input));
        return await SendAsync<LoadPlanGroup>(request);
    }

    public static async Task DeleteLoadPlanGroupAsync(int id, int optionId, int groupId) =>
        await SendAsync(NewRequest(HttpMethod.Delete, $"api/outward/{id}/load-plan/options/{optionId}/groups/{groupId}"));

    public static async Task<LoadPlanGroup> DuplicateLoadPlanGroupAsync(int id, int optionId, int groupId) =>
        await SendAsync<LoadPlanGroup>(NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/options/{optionId}/groups/{groupId}/duplicate"));

    public static async Task<LoadPlanGroup> SetLoadPlanGroupLockAsync(int id, int optionId, int groupId, bool locked) =>
        await SendAsync<LoadPlanGroup>(NewRequest(HttpMethod.Post,
            $"api/outward/{id}/load-plan/options/{optionId}/groups/{groupId}/{(locked ? "lock" : "unlock")}"));

    public static async Task<List<LoadPlanGroup>> SplitLoadPlanGroupAsync(int id, int optionId, int groupId, int splitQuantity)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/options/{optionId}/groups/{groupId}/split");
        request.Content = JsonContent.Create(new { splitQuantity });
        return await SendAsync<List<LoadPlanGroup>>(request);
    }

    public static async Task<List<LoadGroupSearchResult>> SearchLoadPlanGroupsAsync(int id, int optionId, string query) =>
        await SendAsync<List<LoadGroupSearchResult>>(NewRequest(HttpMethod.Get,
            $"api/outward/{id}/load-plan/options/{optionId}/groups/search?query={Uri.EscapeDataString(query)}"));

    public static async Task<List<LoadPlanGroup>> CompactLoadPlanGroupsAsync(int id, int optionId) =>
        await SendAsync<List<LoadPlanGroup>>(NewRequest(HttpMethod.Post,
            $"api/outward/{id}/load-plan/options/{optionId}/groups/compact"));

    private static object ToRequestBody(PlaceLoadGroupInput input) => new
    {
        dispatchOrderLineId = input.DispatchOrderLineId,
        quantity = input.Quantity,
        zoneLength = input.ZoneLength,
        zoneWidth = input.ZoneWidth,
        excludeGroupId = input.ExcludeGroupId
    };

    public static async Task<LoadPlanValidation> ValidateLoadPlanOptionAsync(int id, int optionId) =>
        await SendAsync<LoadPlanValidation>(NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/options/{optionId}/validate"));

    // ---------- 3D load plan: actual loading confirmation ----------

    public static async Task StartLoadPlanConfirmationAsync(int id) =>
        await SendAsync(NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/confirm/start"));

    public static async Task<List<LoadConfirmationStep>> GetLoadPlanConfirmationStepsAsync(int id) =>
        await SendAsync<List<LoadConfirmationStep>>(NewRequest(HttpMethod.Get, $"api/outward/{id}/load-plan/confirm/steps"));

    public static async Task<LoadConfirmationStep> StartLoadPlanGroupAsync(int id, int groupId) =>
        await SendAsync<LoadConfirmationStep>(NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/confirm/groups/{groupId}/start"));

    public static async Task<LoadConfirmationStep> MarkLoadPlanGroupLoadedAsync(int id, int groupId, int? actualQuantity)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/confirm/groups/{groupId}/loaded");
        request.Content = JsonContent.Create(new { actualQuantity });
        return await SendAsync<LoadConfirmationStep>(request);
    }

    public static async Task<LoadConfirmationStep> MarkLoadPlanGroupMismatchAsync(int id, int groupId, int? actualQuantity, string notes)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/confirm/groups/{groupId}/mismatch");
        request.Content = JsonContent.Create(new { actualQuantity, notes });
        return await SendAsync<LoadConfirmationStep>(request);
    }

    public static async Task<LoadConfirmationStep> MarkLoadPlanGroupShortLoadAsync(int id, int groupId, int actualQuantity, string? notes)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/confirm/groups/{groupId}/short-load");
        request.Content = JsonContent.Create(new { actualQuantity, notes });
        return await SendAsync<LoadConfirmationStep>(request);
    }

    public static async Task<LoadConfirmationStep> SkipLoadPlanGroupAsync(int id, int groupId, string notes)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/confirm/groups/{groupId}/skip");
        request.Content = JsonContent.Create(new { notes });
        return await SendAsync<LoadConfirmationStep>(request);
    }

    public static async Task<LoadConfirmationStep> AddLoadPlanGroupPhotoAsync(int id, int groupId, string filePath)
    {
        var request = NewRequest(HttpMethod.Post, $"api/outward/{id}/load-plan/confirm/groups/{groupId}/photo");

        var fileBytes = await File.ReadAllBytesAsync(filePath);
        var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fileBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(fileContent, "file", Path.GetFileName(filePath));

        request.Content = content;
        return await SendAsync<LoadConfirmationStep>(request);
    }
}
