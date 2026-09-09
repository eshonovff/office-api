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

    // 2026-09-09 production: канал бо external_id=27208597255483913 (майдони "id"-и /me) сохта
    // шуд, вале webhook entry[0].id=17841437397996064 мефиристод — канал ҳеҷ гоҳ ёфт намешуд.
    // Fixture-и воқеӣ, айнан аз curl-и зинда (GET /me?fields=id,user_id,username).
    private const string RealMeFixture =
        """{"id":"27208597255483913","user_id":"17841437397996064","username":"crmnizom.tj"}""";

    [Fact]
    public void ExtractBusinessAccountId_RealFixture_ReturnsUserIdFieldNotIdField()
    {
        using var doc = JsonDocument.Parse(RealMeFixture);

        var businessAccountId = InstagramOAuthConnector.ExtractBusinessAccountId(doc.RootElement);

        Assert.Equal("17841437397996064", businessAccountId);
        Assert.NotEqual(doc.RootElement.GetProperty("id").GetString(), businessAccountId);
    }

    [Fact]
    public void ExtractBusinessAccountId_MissingUserIdField_ReturnsNull()
    {
        using var doc = JsonDocument.Parse("""{"id":"27208597255483913","username":"crmnizom.tj"}""");

        Assert.Null(InstagramOAuthConnector.ExtractBusinessAccountId(doc.RootElement));
    }
}
