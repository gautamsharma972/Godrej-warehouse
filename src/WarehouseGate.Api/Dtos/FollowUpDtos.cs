namespace WarehouseGate.Api.Dtos;

public record FollowUpTaskDto(
    int Id,
    string Type,
    string Status,
    string EntityName,
    int EntityId,
    string Title,
    string Details,
    DateTime CreatedAtUtc,
    string? ResolvedByName,
    DateTime? ResolvedAtUtc,
    string? ResolutionNotes);

public record ResolveFollowUpRequest(string? Notes);
