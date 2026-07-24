namespace WarehouseGate.Api.Dtos;

public record LoginRequest(string UserName, string Password);

public record LoginResponse(
    string Token,
    string Role,
    string DisplayName,
    DateTime ExpiresAtUtc,
    string? WarehouseName,
    string? RegionName);
