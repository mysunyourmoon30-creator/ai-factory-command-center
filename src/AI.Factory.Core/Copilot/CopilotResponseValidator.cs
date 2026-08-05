using System.Text.Json;
using AI.Factory.Core.Domain;

namespace AI.Factory.Core.Copilot;

/// <summary>
/// Validates the model's structured-output JSON against the locked shape (§10.4). Any failure —
/// malformed JSON, missing required field, invalid enum, an item over its length/count cap —
/// fails closed: the caller falls back rather than showing unvalidated model output.
/// </summary>
public static class CopilotResponseValidator
{
    public const int MaxSummaryLength = 1000;
    public const int MaxActionLength = 300;
    public const int MaxAffectedOrders = 20;
    public const int MaxRecommendedActions = 10;

    public static bool TryValidate(string? json, out CopilotResponseDto? response)
    {
        response = null;
        if (string.IsNullOrWhiteSpace(json)) return false;

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

            if (!TryReadStringArray(root, "affectedOrders", MaxAffectedOrders, int.MaxValue, out var affectedOrders)) return false;
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
