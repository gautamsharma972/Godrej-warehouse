using System.Security.Claims;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Services;

// Reads the "orgId" claim TokenService stamps onto every JWT. HasContext is true whenever an
// authenticated request is in flight (even for PlatformAdmin, whose OrganizationId is then
// null - "authenticated but no org" is a real, filtered state, not "scoping disabled"). Outside
// a request (SeedData, migrations, background work) there is no HttpContext at all, so
// HasContext is false and WarehouseGateDbContext's tenant query filters are bypassed entirely.
public class HttpContextCurrentTenantProvider : ICurrentTenantProvider
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentTenantProvider(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public bool HasContext => User?.Identity?.IsAuthenticated == true;

    public int? OrganizationId
    {
        get
        {
            var raw = User?.FindFirst("orgId")?.Value;
            return int.TryParse(raw, out var id) ? id : null;
        }
    }
}
