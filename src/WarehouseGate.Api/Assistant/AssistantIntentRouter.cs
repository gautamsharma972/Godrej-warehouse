namespace WarehouseGate.Api.Assistant;

// Deterministic pre-routing for free-text chat messages, added after a realistic-query stress
// test found the LLM's own tool selection specifically unreliable for FORM-triggering write
// actions - see the real bug named in each rule below. Scoped deliberately narrow: only
// capabilities where (a) a wrong match is low-cost (worst case, an unwanted form the user can
// just ignore) and (b) the tool needs no parameter extracted from the message text at all (the
// form captures real values afterward via its own dropdowns, so there's nothing an LLM's parsing
// could add that a keyword match doesn't already have). Read/list capabilities (active jobs,
// follow-ups, performance, in-transit vehicles) are deliberately NOT included - their tools accept
// optional filters (status, vehicle number) that only the LLM can extract from arbitrary phrasing,
// so pre-routing those would silently throw that filtering away. When this returns a capability
// the caller's role doesn't actually have a plugin for, AssistantController gives the same honest
// decline the LLM path would - the point is removing the LLM's unreliable tool-selection step for
// these specific cases, not bypassing the existing role gating.
public static class AssistantIntentRouter
{
    public static string? Match(string message)
    {
        var text = message.ToLowerInvariant();

        // "resolve follow up 5" (SuperAdmin, who has no such tool) got "I can help with that -
        // please provide more details", a false offer with no tool call behind it, non-
        // deterministically about 1 turn in 3 even after broadening the system prompt's action-verb
        // list. There's no reliable text signature to catch that reply after the fact, so removing
        // the LLM from this decision entirely is the only way to make it 100% consistent.
        if (text.Contains("follow") && ContainsAny(text, "resolve", "close", "mark done", "mark resolved"))
        {
            return "followups.resolve";
        }

        // Only matches once the job type is actually named - "assign someone to job 5" alone stays
        // genuinely ambiguous between the inward and outward form, and asking the user to clarify
        // which one is the correct behavior, not something pre-routing should try to guess.
        if (text.Contains("assign") && text.Contains("supervisor"))
        {
            if (ContainsAny(text, "inward", "inbound", "receiving"))
            {
                return "supervisors.assign.inward";
            }
            if (ContainsAny(text, "outward", "outbound"))
            {
                return "supervisors.assign.outward";
            }
        }

        // Quantity-update checked first - both phrasings mention "pick list", only the verb tells
        // them apart. "I need to modify picklist quantity" got a reply describing a form that was
        // never actually attached (the model narrated the tool's effect without calling it),
        // intermittently - a keyword match always calls the real tool, no narration possible.
        if (ContainsAny(text, "pick list", "picklist"))
        {
            if (ContainsAny(text, "quantity", "modify", "update", "change"))
            {
                return "picklist.quantity";
            }
            if (ContainsAny(text, "generate", "create"))
            {
                return "picklist.generate";
            }
        }

        // Import checked first - both phrasings mention "dispatch plan", only the verb tells them
        // apart. "import dispatch plan" opened the single-entry CREATE form instead of the Excel
        // file picker most of the time, even after strengthening both tools' descriptions to
        // explicitly disambiguate on the word "import" - a keyword match removes the ambiguity
        // instead of hoping the model resolves it correctly.
        if (text.Contains("dispatch plan"))
        {
            if (ContainsAny(text, "import", "upload", "bulk", "excel", "spreadsheet"))
            {
                return "dispatchplan.import";
            }
            if (ContainsAny(text, "create", "add", "new"))
            {
                return "dispatchplan.create";
            }
        }

        return null;
    }

    private static bool ContainsAny(string text, params string[] terms) => terms.Any(text.Contains);
}
