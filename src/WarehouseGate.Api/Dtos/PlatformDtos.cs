namespace WarehouseGate.Api.Dtos;

public record OrganizationDto(
    int Id,
    string Name,
    string Code,
    bool IsActive,
    int MaxUsers,
    int MaxWarehouses,
    int UserCount,
    int WarehouseCount,
    DateTime CreatedAtUtc);

// Creates the Organization and its first SuperAdmin user together - that SuperAdmin is who then
// signs in (with Code, AdminUserName, AdminPassword) to set up the rest of the org's masters.
public record CreateOrganizationRequest(
    string Name,
    string Code,
    int MaxUsers,
    int MaxWarehouses,
    string AdminUserName,
    string AdminDisplayName,
    string AdminPassword);

public record UpdateOrganizationRequest(string Name, bool IsActive, int MaxUsers, int MaxWarehouses);

public record PlatformUserDto(
    string Id,
    string UserName,
    string DisplayName,
    string Role,
    int? OrganizationId,
    string OrganizationName);

public record ResetPasswordRequest(string NewPassword);
