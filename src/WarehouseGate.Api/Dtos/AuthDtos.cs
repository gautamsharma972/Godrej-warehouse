namespace WarehouseGate.Api.Dtos;

// OrganizationCode disambiguates which org's account "UserName" refers to, since usernames are
// only unique per-organization (see AuthController.Login). Optional so older mobile app builds
// that only send UserName/Password keep working as long as that username is unambiguous across
// organizations.
public record LoginRequest(string UserName, string Password, string? OrganizationCode = null);

public record LoginResponse(
    string Token,
    string Role,
    string DisplayName,
    DateTime ExpiresAtUtc,
    string? WarehouseName,
    string? RegionName,
    string? OrganizationName);
