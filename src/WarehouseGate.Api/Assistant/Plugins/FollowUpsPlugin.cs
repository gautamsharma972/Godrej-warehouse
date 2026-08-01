using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.SemanticKernel;
using WarehouseGate.Api.Dtos;
using WarehouseGate.Domain;
using WarehouseGate.Infrastructure;

namespace WarehouseGate.Api.Assistant.Plugins;

// Unlike Outward/InwardJobsPlugin, there's no standalone FollowUpService to wrap - the query lives
// directly in OfficeController.GetFollowUps(). Same List<int>? warehouse-scope convention as the
// other plugins (null = unscoped for SuperAdmin) instead of a single warehouse id. Same clickable-
// row convention too - see OutwardJobsPlugin's header comment for why rows are only actionable
// (into the Resolve Follow-up form) for Office, never SuperAdmin.
public class FollowUpsPlugin
{
    private const int MaxResults = 200;
    private const int MaxListItems = 50;

    private readonly WarehouseGateDbContext _db;
    private readonly List<int>? _warehouseScope;

    public FollowUpsPlugin(WarehouseGateDbContext db, List<int>? warehouseScope)
    {
        _db = db;
        _warehouseScope = warehouseScope;
    }

    public AssistantListResultDto? LastListResult { get; private set; }

    [KernelFunction("get_open_follow_ups")]
    [Description(
        "Gets open follow-up to-dos visible to the caller - their own warehouse for Office, every " +
        "warehouse for SuperAdmin - exception GRNs awaiting supplier follow-up and partial loads needing " +
        "a stock transfer-out note. When the user asks to narrow this down (e.g. \"only the partial " +
        "loads\"), call this AGAIN with the type filled in - never just repeat the unfiltered list or " +
        "try to filter it yourself in your reply.")]
    public async Task<string> GetOpenFollowUpsAsync(
        [Description("Optional - only show this type (InwardException or PartialLoadDispatch). Leave blank for every type.")] string? type = null)
    {
        if (_warehouseScope is not null && _warehouseScope.Count == 0)
        {
            return "The caller has no warehouse assigned, so no follow-ups can be listed.";
        }

        var query = _db.FollowUpTasks.Where(t => t.Status == FollowUpStatus.Open);
        if (_warehouseScope is not null)
        {
            query = query.Where(t => t.WarehouseId != null && _warehouseScope.Contains(t.WarehouseId.Value));
        }

        string? filterSuffix = null;
        string? warningSuffix = null;

        if (!string.IsNullOrWhiteSpace(type))
        {
            if (EnumFilterHelper.TryMatch<FollowUpType>(type, out var typeFilter))
            {
                query = query.Where(t => t.Type == typeFilter);
                filterSuffix = $" (type: {typeFilter})";
            }
            else
            {
                warningSuffix = $" - type '{type}' isn't recognized, showing every type instead";
            }
        }

        var tasks = await query.OrderByDescending(t => t.CreatedAtUtc).Take(MaxResults).ToListAsync();
        if (tasks.Count == 0)
        {
            // The guidance sentence is bracketed as "[Assistant note...]" rather than appended as
            // plain trailing text - see OutwardJobsPlugin's identical zero-count branch for why
            // (a real observed failure: the model echoing this whole return string, guidance
            // included, verbatim as its reply instead of composing its own sentence).
            return $"Zero open follow-ups match{filterSuffix}.{warningSuffix} [Assistant note: tell the " +
                   "user plainly that there are none right now - never say \"here are the follow-ups\" " +
                   "or anything implying results exist when this count is zero. Never repeat this " +
                   "bracketed note itself in your reply.]";
        }

        var canResolve = _warehouseScope is not null;
        var items = tasks.Take(MaxListItems)
            .Select(t => new AssistantListItemDto(
                t.Title,
                $"{t.EntityName} #{t.EntityId} - {t.Details}",
                t.Type.ToString(),
                canResolve ? "resolve-follow-up" : null,
                canResolve ? t.Id.ToString() : null,
                canResolve ? "/office/follow-ups" : null,
                canResolve ? "Open follow-ups" : null))
            .ToList();
        LastListResult = new AssistantListResultDto($"{tasks.Count} open follow-up(s){filterSuffix}", items, tasks.Count);

        // See the zero-count branch above for why the guidance clause is bracketed.
        return $"{tasks.Count} open follow-up(s) found{filterSuffix} - already shown to the user as a " +
               $"list.{warningSuffix} [Assistant note: just acknowledge briefly, don't restate the " +
               "details, and never repeat this bracketed note itself in your reply.]";
    }
}
