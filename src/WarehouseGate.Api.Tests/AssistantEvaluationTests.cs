using System.Security.Claims;
using Microsoft.Extensions.Logging.Abstractions;
using WarehouseGate.Api.Assistant;

namespace WarehouseGate.Api.Tests;

// Deterministic foundation for the Assistant evaluation suite. These cases guard the capability
// selection and feedback boundaries without requiring a live Ollama instance; model/tool-routing
// prompt cases can be added alongside them once a repeatable model test host is available.
public class AssistantEvaluationTests
{
    [Fact]
    public void OfficeJobPage_PrioritizesContextualCapabilities()
    {
        var capabilities = AssistantCapabilityRegistry.ForUser(
            User("office-user", "Office"),
            "/office/outward-jobs/42");

        Assert.Equal("Summarize this job", capabilities[0].Label);
        Assert.Equal("What happens next?", capabilities[1].Label);
        Assert.Equal("job.diagnose", capabilities[2].CapabilityId);
        Assert.Equal("supervisors.assign.outward", capabilities[3].CapabilityId);
        Assert.Equal(
            capabilities.Count,
            capabilities.Select(c => c.CapabilityId ?? c.Prompt).Distinct().Count());
    }

    [Fact]
    public void LogisticsUser_DoesNotReceiveOfficeCapabilities()
    {
        var capabilities = AssistantCapabilityRegistry.ForUser(
            User("logistics-user", "LogisticsManager"),
            "/logistics/vehicle-records");

        Assert.Contains(capabilities, c => c.CapabilityId == "logistics.intransit");
        Assert.Contains(capabilities, c => c.CapabilityId == "dispatchplan.create");
        Assert.DoesNotContain(capabilities, c => c.CapabilityId == "outward.active");
        Assert.DoesNotContain(capabilities, c => c.CapabilityId == "supervisors.assign.outward");
    }

    [Fact]
    public void OfficeDashboard_OffersDeterministicOperationsBriefingFirst()
    {
        var capabilities = AssistantCapabilityRegistry.ForUser(
            User("office-user", "Office"),
            "/office/dashboard");

        Assert.Equal("operations.briefing", capabilities[0].CapabilityId);
        Assert.Equal("Live operations briefing", capabilities[0].Label);
    }

    [Fact]
    public void Feedback_IsBoundToTheUserWhoReceivedTheTurn()
    {
        var telemetry = new AssistantTelemetry(NullLogger<AssistantTelemetry>.Instance);
        var turnId = telemetry.RecordTurn("user-a", "capability", "outward.active", true, 125);

        Assert.False(telemetry.RecordFeedback(turnId, "user-b", true));
        Assert.True(telemetry.RecordFeedback(turnId, "user-a", true));

        var metrics = telemetry.Snapshot();
        Assert.Equal(1, metrics.TotalTurns);
        Assert.Equal(1, metrics.DeterministicTurns);
        Assert.Equal(1, metrics.HelpfulRatings);
        Assert.Equal(125, metrics.AverageLatencyMs);
    }

    private static ClaimsPrincipal User(string userId, string role) =>
        new(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Role, role)
        ], "test"));
}
