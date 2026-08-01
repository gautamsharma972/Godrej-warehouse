using System.Security.Claims;
using WarehouseGate.Api.Dtos;

namespace WarehouseGate.Api.Assistant;

// Single source of truth for the actions advertised by the Assistant UI. Authorization still
// remains enforced by AssistantController and the scoped plugins; this registry only determines
// which valid capabilities should be presented to a caller on a given page.
public static class AssistantCapabilityRegistry
{
    private sealed record Capability(
        string Label,
        string Prompt,
        string? Id,
        string Role);

    private static readonly Capability[] Capabilities =
    [
        new("Live operations briefing", "Show me the live operations briefing.", "operations.briefing", "Office"),
        new("Active outward jobs", "Show me the active outward jobs.", "outward.active", "Office"),
        new("Active inward jobs", "Show me the active inward jobs.", "inward.active", "Office"),
        new("Open follow-ups", "Show me the open follow-ups.", "followups.open", "Office"),
        new("Resolve a follow-up", "I'd like to resolve a follow-up.", "followups.resolve", "Office"),
        new("Assign inward supervisor", "Assign a supervisor to an inward job.", "supervisors.assign.inward", "Office"),
        new("Assign outward supervisor", "Assign a supervisor to an outward job.", "supervisors.assign.outward", "Office"),
        new("Generate pick list", "Generate a pick list for a vehicle.", "picklist.generate", "Office"),
        new("Update pick list quantity", "Update a pick list quantity.", "picklist.quantity", "Office"),
        new("Supervisor performance", "Show me the supervisor performance report.", "supervisors.performance", "Office"),

        new("Vehicles in transit", "Show me the vehicles currently in transit.", "logistics.intransit", "LogisticsManager"),
        new("Create Dispatch Plan entry", "Let's create a new Dispatch Plan entry.", "dispatchplan.create", "LogisticsManager"),
        new("Import Dispatch Plan", "I want to import a Dispatch Plan from Excel.", "dispatchplan.import", "LogisticsManager"),

        new("Live operations briefing", "Show the live operations briefing across all warehouses.", "operations.briefing", "SuperAdmin"),
        new("Active outward jobs", "Show active outward jobs across all warehouses.", "outward.active", "SuperAdmin"),
        new("Active inward jobs", "Show active inward jobs across all warehouses.", "inward.active", "SuperAdmin"),
        new("Open follow-ups", "Show open follow-ups across all warehouses.", "followups.open", "SuperAdmin"),
        new("Vehicles in transit", "Show vehicles currently in transit.", "logistics.intransit", "SuperAdmin"),
        new("Supervisor performance", "Show the supervisor performance report.", "supervisors.performance", "SuperAdmin"),
        new("Create Dispatch Plan entry", "Let's create a new Dispatch Plan entry.", "dispatchplan.create", "SuperAdmin"),
        new("Import Dispatch Plan", "I want to import a Dispatch Plan from Excel.", "dispatchplan.import", "SuperAdmin")
    ];

    public static IReadOnlyList<AssistantCapabilityDto> ForUser(ClaimsPrincipal user, string? rawPath)
    {
        var path = NormalizePath(rawPath);
        var contextual = ContextualCapabilities(user, path);
        var roleCapabilities = Capabilities
            .Where(c => user.IsInRole(c.Role))
            .Select(c => new AssistantCapabilityDto(c.Label, c.Prompt, c.Id, false));

        return contextual
            .Concat(roleCapabilities)
            .DistinctBy(c => c.CapabilityId ?? c.Prompt)
            .ToList();
    }

    private static IEnumerable<AssistantCapabilityDto> ContextualCapabilities(
        ClaimsPrincipal user,
        string path)
    {
        if (!user.IsInRole("Office"))
        {
            return [];
        }

        if (path.StartsWith("/office/inward-jobs/", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("Summarize this job", "Summarize this job for me.", null, true),
                new("What happens next?", "What should happen next for this job?", null, true),
                new("Diagnose blockers", "Explain what is blocking this job.", "job.diagnose", true),
                new("Assign supervisor", "Assign a supervisor to this inward job.", "supervisors.assign.inward", true)
            ];
        }

        if (path.StartsWith("/office/outward-jobs/", StringComparison.OrdinalIgnoreCase))
        {
            return
            [
                new("Summarize this job", "Summarize this job for me.", null, true),
                new("What happens next?", "What should happen next for this job?", null, true),
                new("Diagnose blockers", "Explain what is blocking this job.", "job.diagnose", true),
                new("Assign supervisor", "Assign a supervisor to this outward job.", "supervisors.assign.outward", true)
            ];
        }

        return path.ToLowerInvariant() switch
        {
            "/office/inward-jobs" =>
            [
                new("Active inward jobs", "Show me the active inward jobs.", "inward.active", true),
                new("Assign inward supervisor", "Assign a supervisor to an inward job.", "supervisors.assign.inward", true)
            ],
            "/office/outward-jobs" =>
            [
                new("Active outward jobs", "Show me the active outward jobs.", "outward.active", true),
                new("Assign outward supervisor", "Assign a supervisor to an outward job.", "supervisors.assign.outward", true),
                new("Generate pick list", "Generate a pick list for a vehicle.", "picklist.generate", true)
            ],
            "/office/follow-ups" =>
            [
                new("Open follow-ups", "Show me the open follow-ups.", "followups.open", true),
                new("Resolve a follow-up", "I'd like to resolve a follow-up.", "followups.resolve", true)
            ],
            _ => []
        };
    }

    private static string NormalizePath(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return "/";
        }

        var path = rawPath.Trim().Split('?', '#')[0];
        if (!path.StartsWith('/') || path.Length > 256 || path.Contains("://", StringComparison.Ordinal))
        {
            return "/";
        }

        var normalized = path.TrimEnd('/');
        return normalized.Length == 0 ? "/" : normalized;
    }
}
