using System.Text.Json;
using Office.Api.Channels.Meta;

namespace Office.Api.Tests.Channels.Meta;

public class InstagramOAuthConnectorTests
{
    // 2026-09-08 production: се маротиба афтод бо InvalidOperationException
    // ("target element has type 'Number'") — Meta баъзан user_id-ро ҳамчун JSON
    // number бармегардонад, на string тавре ки ҳуҷҷат нишон медиҳад.
    [Fact]
    public void ExtractTokenAndUserId_FlatPayloadWithStringUserId_ReturnsUserId()
    {
        using var doc = JsonDocument.Parse("""{"access_token":"tok-abc","user_id":"12345"}""");

        var (token, userId) = InstagramOAuthConnector.ExtractTokenAndUserId(doc.RootElement);

        Assert.Equal("tok-abc", token);
        Assert.Equal("12345", userId);
    }

    [Fact]
    public void ExtractTokenAndUserId_FlatPayloadWithNumericUserId_ReturnsUserIdAsString()
    {
        using var doc = JsonDocument.Parse("""{"access_token":"tok-abc","user_id":12345}""");

        var (token, userId) = InstagramOAuthConnector.ExtractTokenAndUserId(doc.RootElement);

        Assert.Equal("tok-abc", token);
        Assert.Equal("12345", userId);
    }

    [Fact]
    public void ExtractTokenAndUserId_NestedDataPayloadWithStringUserId_ReturnsUserId()
    {
        using var doc = JsonDocument.Parse("""{"data":[{"access_token":"tok-abc","user_id":"12345"}]}""");

        var (token, userId) = InstagramOAuthConnector.ExtractTokenAndUserId(doc.RootElement);

        Assert.Equal("tok-abc", token);
        Assert.Equal("12345", userId);
    }

    [Fact]
    public void ExtractTokenAndUserId_NestedDataPayloadWithNumericUserId_ReturnsUserIdAsString()
    {
        using var doc = JsonDocument.Parse("""{"data":[{"access_token":"tok-abc","user_id":12345}]}""");

        var (token, userId) = InstagramOAuthConnector.ExtractTokenAndUserId(doc.RootElement);

        Assert.Equal("tok-abc", token);
        Assert.Equal("12345", userId);
    }

    [Fact]
    public void ExtractTokenAndUserId_PayloadWithoutUserId_ReturnsNullUserId()
    {
        using var doc = JsonDocument.Parse("""{"access_token":"tok-abc"}""");

        var (token, userId) = InstagramOAuthConnector.ExtractTokenAndUserId(doc.RootElement);

        Assert.Equal("tok-abc", token);
        Assert.Null(userId);
    }

    [Fact]
    public void ExtractTokenAndUserId_MalformedPayloadWithoutAccessToken_ThrowsInsteadOfSilentlyFailing()
    {
        using var doc = JsonDocument.Parse("""{"user_id":"12345"}""");

        Assert.Throws<KeyNotFoundException>(() => InstagramOAuthConnector.ExtractTokenAndUserId(doc.RootElement));
    }
}
