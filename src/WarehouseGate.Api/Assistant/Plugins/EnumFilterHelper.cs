namespace WarehouseGate.Api.Assistant.Plugins;

// Shared by every read-only plugin's optional status/type filter parameter. A small local model
// won't always transcribe an enum name back into a tool argument exactly right ("Pick List
// Generated", "picklist-generated", "PICKLISTGENERATED" should all still match OutwardStatus.
// PickListGenerated) - matching on letters/digits only, case-insensitive, absorbs all of that
// instead of requiring an exact string match that quietly filters out everything.
internal static class EnumFilterHelper
{
    public static bool TryMatch<TEnum>(string? input, out TEnum value) where TEnum : struct, Enum
    {
        value = default;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var normalizedInput = Normalize(input);
        foreach (var candidate in Enum.GetValues<TEnum>())
        {
            if (Normalize(candidate.ToString()) == normalizedInput)
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }

    private static string Normalize(string text) =>
        new string(text.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
