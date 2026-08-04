namespace WarehouseGate.Api.Dtos;

public record AssistantChatTurnDto(string Role, string Content);

public record AssistantPageContextDto(string Path);

public record AssistantCapabilityDto(
    string Label,
    string Prompt,
    string? CapabilityId,
    bool IsContextual);

public record AssistantChatRequest(
    string Message,
    Guid? ConversationId = null,
    AssistantPageContextDto? PageContext = null,
    string? CapabilityId = null,
    List<AssistantChatTurnDto>? History = null);

// Non-null only when the assistant just validated something it can write (see
// DispatchPlanCreationPlugin's header comment for why this is a real button, not a chat reply the
// model can trigger itself) - the web widget renders Confirm/Cancel for the user to act on.
public record AssistantPendingConfirmationDto(string Token, string Summary, string ActionType);

// One <select> choice - Value is what gets submitted back (a name for Dispatch Plan's warehouse
// fields, a database ID for anything that's really an entity picker like "which follow-up").
// Label is what the user sees, which for entity pickers is usually more descriptive than Value.
public record AssistantFormOptionDto(string Value, string Label);

// Describes one input on a structured form the widget should render instead of asking for the
// field in a chat message - Type drives which HTML control the widget picks ("text", "number",
// "select", "datetime"); Options is only populated for "select" and comes straight from the DB,
// never from the model, so it can't be wrong or hallucinated.
public record AssistantFormFieldDto(
    string Name,
    string Label,
    string Type,
    bool Required,
    List<AssistantFormOptionDto>? Options = null,
    string? DefaultValue = null);

public record AssistantFormRequestDto(string FormType, List<AssistantFormFieldDto> Fields);

// One row in a read-only result list the widget renders as a card instead of the model retyping
// it as prose. ActionType/ActionValue are non-null only when the row is clickable (e.g. a follow-up
// row can jump straight into the Resolve Follow-up form with that follow-up pre-selected) - null
// means the row is display-only (no matching action exists yet, e.g. in-transit vehicles).
public record AssistantListItemDto(
    string Title,
    string? Subtitle,
    string? Badge,
    string? ActionType = null,
    string? ActionValue = null,
    string? NavigationUrl = null,
    string? NavigationLabel = null);

public record AssistantListResultDto(string Title, List<AssistantListItemDto> Items, int TotalCount);

public record AssistantMetricDto(
    string Label,
    string Value,
    string? Detail = null,
    string? Tone = null);

// Ordered response building blocks. Keeping each payload typed avoids polymorphic JSON metadata
// while allowing a response to contain several lists/forms/actions in a deliberate sequence.
public record AssistantUiBlockDto(
    string Type,
    AssistantListResultDto? ListResult = null,
    AssistantFormRequestDto? FormRequest = null,
    AssistantPendingConfirmationDto? Confirmation = null,
    string? Title = null,
    List<AssistantMetricDto>? Metrics = null);

public record AssistantChatResponseDto(
    string Reply,
    AssistantPendingConfirmationDto? PendingConfirmation = null,
    AssistantFormRequestDto? FormRequest = null,
    AssistantListResultDto? ListResult = null,
    Guid? ConversationId = null,
    List<AssistantUiBlockDto>? Blocks = null,
    Guid? TurnId = null);

public record AssistantFeedbackRequest(Guid TurnId, bool Helpful);
public record AssistantFeedbackResponseDto(bool Recorded);
public record AssistantMetricsDto(
    DateTime SinceUtc,
    int TotalTurns,
    int ModelTurns,
    int DeterministicTurns,
    int SuccessfulTurns,
    int FailedTurns,
    int HelpfulRatings,
    int NotHelpfulRatings,
    double AverageLatencyMs,
    Dictionary<string, int> CapabilityUsage,
    Dictionary<string, int> WorkflowOutcomes);

public record AssistantConfirmActionRequest(string Token, string ActionType);

// Structured submission from the Dispatch Plan form itself - bypasses the LLM entirely, since the
// data already came from real inputs/dropdowns rather than something that needed parsing out of a
// sentence.
public record DispatchPlanFormSubmitRequest(
    string VehicleNumber, string FromWarehouseName, string ToWarehouseName, string Sku, int BoxQuantity,
    string? PoNumber, string? TransporterName, string? DriverName, string? DriverPhone, string? VehicleType,
    string? DepartureDate, string? EtaDateTime);

// Structured submission from the Resolve Follow-up form - the dropdown value is the follow-up's
// own ID, never something the model has to transcribe.
public record ResolveFollowUpFormSubmitRequest(int FollowUpId, string? Notes);

// Structured submission from the Assign Supervisor form - JobType is "Inward" or "Outward" (set by
// the widget from which form type was rendered), both dropdown values are real IDs.
public record AssignSupervisorFormSubmitRequest(string JobType, int JobId, string SupervisorUserId);

// Structured submissions from the two Pick List forms - the dropdown values are a real PO Number
// (already scoped to the caller's own pending rows) and a real VehicleLogisticsRecord ID.
public record GeneratePickListFormSubmitRequest(string PoNumber);
public record UpdatePickListQuantityFormSubmitRequest(int LineId, int Quantity);
