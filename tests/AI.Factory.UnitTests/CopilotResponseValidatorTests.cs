using AI.Factory.Core.Copilot;
using AI.Factory.Core.Domain;

namespace AI.Factory.UnitTests;

public sealed class CopilotResponseValidatorTests
{
    [Fact]
    public void A_full_valid_response_passes()
    {
        var json = """
            {"summary": "One material is short.", "riskLevel": "Critical", "affectedOrders": ["SO-DEMO-001"], "recommendedActions": ["Check the purchasing plan for RM-001"]}
            """;

        Assert.True(CopilotResponseValidator.TryValidate(json, out var response));
        Assert.Equal("One material is short.", response!.Summary);
        Assert.Equal(RiskStatus.Critical, response.RiskLevel);
        Assert.Equal(["SO-DEMO-001"], response.AffectedOrders);
        Assert.False(response.IsFallback);
    }

    [Fact]
    public void Risk_level_and_arrays_are_optional()
    {
        var json = """{"summary": "Nothing to report."}""";

        Assert.True(CopilotResponseValidator.TryValidate(json, out var response));
        Assert.Null(response!.RiskLevel);
        Assert.Empty(response.AffectedOrders);
        Assert.Empty(response.RecommendedActions);
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("[1,2,3]")]
    public void Malformed_or_non_object_input_fails(string? json) =>
        Assert.False(CopilotResponseValidator.TryValidate(json, out _));

    [Fact]
    public void Missing_summary_fails() =>
        Assert.False(CopilotResponseValidator.TryValidate("""{"riskLevel": "Normal"}""", out _));

    [Fact]
    public void Empty_summary_fails() =>
        Assert.False(CopilotResponseValidator.TryValidate("""{"summary": "   "}""", out _));

    [Fact]
    public void Invalid_risk_level_enum_fails() =>
        Assert.False(CopilotResponseValidator.TryValidate("""{"summary": "x", "riskLevel": "SuperBad"}""", out _));

    [Fact]
    public void Oversized_summary_fails()
    {
        var oversized = new string('a', CopilotResponseValidator.MaxSummaryLength + 1);
        Assert.False(CopilotResponseValidator.TryValidate($$"""{"summary": "{{oversized}}"}""", out _));
    }

    [Fact]
    public void Too_many_affected_orders_fails()
    {
        var items = string.Join(',', Enumerable.Range(1, CopilotResponseValidator.MaxAffectedOrders + 1).Select(i => $"\"SO-{i}\""));
        Assert.False(CopilotResponseValidator.TryValidate($$"""{"summary": "x", "affectedOrders": [{{items}}]}""", out _));
    }

    [Fact]
    public void Oversized_recommended_action_fails()
    {
        var oversized = new string('a', CopilotResponseValidator.MaxActionLength + 1);
        Assert.False(CopilotResponseValidator.TryValidate($$"""{"summary": "x", "recommendedActions": ["{{oversized}}"]}""", out _));
    }

    [Fact]
    public void A_non_string_item_in_an_array_fails() =>
        Assert.False(CopilotResponseValidator.TryValidate("""{"summary": "x", "affectedOrders": [1, 2]}""", out _));
}
