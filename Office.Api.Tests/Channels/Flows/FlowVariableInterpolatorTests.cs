using Office.Api.Channels.Flows;

namespace Office.Api.Tests.Channels.Flows;

public class FlowVariableInterpolatorTests
{
    private static readonly Dictionary<string, string> ContactFields = new()
    {
        ["firstName"] = "Фаридун",
        ["username"] = "eshonov.f1",
    };

    [Fact]
    public void Interpolate_ContactField_IsReplaced()
    {
        var result = FlowVariableInterpolator.Interpolate("Салом, {{firstName}}!", new Dictionary<string, string>(), ContactFields);

        Assert.Equal("Салом, Фаридун!", result);
    }

    [Fact]
    public void Interpolate_UserVariable_IsReplaced()
    {
        var variables = new Dictionary<string, string> { ["city"] = "Душанбе" };

        var result = FlowVariableInterpolator.Interpolate("Шаҳр: {{city}}", variables, ContactFields);

        Assert.Equal("Шаҳр: Душанбе", result);
    }

    [Fact]
    public void Interpolate_VariableTakesPrecedenceOverContactField_WhenKeysCollide()
    {
        var variables = new Dictionary<string, string> { ["firstName"] = "Override" };

        var result = FlowVariableInterpolator.Interpolate("{{firstName}}", variables, ContactFields);

        Assert.Equal("Override", result);
    }

    [Fact]
    public void Interpolate_UnknownKey_IsLeftUnchanged()
    {
        var result = FlowVariableInterpolator.Interpolate("{{typoKey}}", new Dictionary<string, string>(), ContactFields);

        Assert.Equal("{{typoKey}}", result);
    }

    [Fact]
    public void Interpolate_MultipleTokens_AllReplaced()
    {
        var result = FlowVariableInterpolator.Interpolate(
            "{{firstName}} (@{{username}})", new Dictionary<string, string>(), ContactFields);

        Assert.Equal("Фаридун (@eshonov.f1)", result);
    }

    [Fact]
    public void Interpolate_NoTokens_ReturnsTextUnchanged()
    {
        var result = FlowVariableInterpolator.Interpolate("Салом ҳама!", new Dictionary<string, string>(), ContactFields);

        Assert.Equal("Салом ҳама!", result);
    }
}
