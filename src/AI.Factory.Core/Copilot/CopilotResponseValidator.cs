using System.Text.Json;
using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Copilot;

/// <summary>
/// Validates the model's structured-output JSON against the locked shape (§10.4). Any failure —
/// malformed JSON, missing required field, invalid enum, an item over its length/count cap —
/// fails closed: the caller falls back rather than showing unvalidated model output.
///
/// <para>
/// This is the only thing standing between model output and the screen: OllamaClient reads the
/// response body whole with no size limit, so every bound the rendered answer has, it has here.
/// </para>
/// </summary>
public static class CopilotResponseValidator
{
    public const int MaxSummaryLength = 1000;
    public const int MaxActionLength = 300;

    /// <summary>
    /// Per-item cap on affectedOrders, which previously had none: the array's *count* was capped
    /// but each entry was accepted at any length, so twenty unbounded strings could pass validation
    /// and reach the page. An order reference is an OrderNumber, itself limited to 30 characters, so
    /// this is already generous.
    /// </summary>
    public const int MaxAffectedOrderLength = 100;

    public const int MaxAffectedOrders = 20;
    public const int MaxRecommendedActions = 10;

    /// <summary>
    /// Refuses an absurd payload before JsonDocument.Parse allocates for it. The largest response
    /// this validator can accept is roughly 6.5 KB (summary + both arrays at their caps), so this
    /// leaves generous headroom while keeping a runaway model from being parsed into memory.
    /// </summary>
    public const int MaxRawResponseLength = 16_000;

    public static bool TryValidate(string? json, out CopilotResponseDto? response)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxRawResponseLength) return false;

        JsonDocument document;
        try { document = JsonDocument.Parse(json); }
        catch (JsonException) { return false; }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;
            if (!root.TryGetProperty("summary", out var summaryElement) || summaryElement.ValueKind != JsonValueKind.String) return false;

            var summary = summaryElement.GetString();
            if (string.IsNullOrWhiteSpace(summary) || summary.Length > MaxSummaryLength) return false;

            RiskStatus? riskLevel = null;
            if (root.TryGetProperty("riskLevel", out var riskElement) && riskElement.ValueKind != JsonValueKind.Null)
            {
                if (riskElement.ValueKind != JsonValueKind.String || !Enum.TryParse<RiskStatus>(riskElement.GetString(), ignoreCase: true, out var parsed))
                    return false;
                riskLevel = parsed;
            }

            if (!TryReadStringArray(root, "affectedOrders", MaxAffectedOrders, MaxAffectedOrderLength, out var affectedOrders)) return false;
            if (!TryReadStringArray(root, "recommendedActions", MaxRecommendedActions, MaxActionLength, out var recommendedActions)) return false;

            response = new CopilotResponseDto(summary.Trim(), riskLevel, affectedOrders, recommendedActions, IsFallback: false);
            return true;
        }
    }

    private static bool TryReadStringArray(JsonElement root, string propertyName, int maxCount, int maxItemLength, out IReadOnlyCollection<string> values)
    {
        values = [];
        if (!root.TryGetProperty(propertyName, out var element) || element.ValueKind == JsonValueKind.Null)
            return true;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() > maxCount) return false;

        var list = new List<string>();
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String) return false;
            var text = item.GetString();
            if (text is null || text.Length > maxItemLength) return false;
            list.Add(text);
        }

        values = list;
        return true;
    }
}
