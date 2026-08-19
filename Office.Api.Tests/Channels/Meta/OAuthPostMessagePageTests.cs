using System.Net;
using System.Text.Json;
using Office.Api.Channels.Meta;

namespace Office.Api.Tests.Channels.Meta;

public class OAuthPostMessagePageTests
{
    [Fact]
    public void Build_EmbedsPayloadThatRoundTripsThroughHtmlDecodeAndJsonParse()
    {
        var payload = new { connectionId = "c1", accounts = new[] { new { externalId = "a1", name = "My Page" } } };

        var html = OAuthPostMessagePage.Build(payload);
        var decoded = WebUtility.HtmlDecode(ExtractAttribute(html, "data-oauth-payload"));
        var parsed = JsonSerializer.Deserialize<JsonElement>(decoded);

        Assert.Equal("c1", parsed.GetProperty("connectionId").GetString());
        Assert.Equal("My Page", parsed.GetProperty("accounts")[0].GetProperty("name").GetString());
    }

    [Fact]
    public void Build_NeutralizesAScriptClosingSequenceInsideAPayloadValue()
    {
        // A hostile account name (or any string field) containing a literal
        // "</script>" must not be able to close the page's own script tag early.
        var payload = new { title = "</script><script>alert(1)</script>" };

        var html = OAuthPostMessagePage.Build(payload);

        // Only the page's own single legitimate closing tag should survive.
        Assert.Equal(1, CountOccurrences(html, "</script", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_StillRoundTripsAPayloadContainingHtmlMetacharacters()
    {
        var payload = new { title = "</script><script>alert(1)</script>", detail = "\"quoted\" & <tagged>" };

        var html = OAuthPostMessagePage.Build(payload);
        var decoded = WebUtility.HtmlDecode(ExtractAttribute(html, "data-oauth-payload"));
        var parsed = JsonSerializer.Deserialize<JsonElement>(decoded);

        Assert.Equal("</script><script>alert(1)</script>", parsed.GetProperty("title").GetString());
        Assert.Equal("\"quoted\" & <tagged>", parsed.GetProperty("detail").GetString());
    }

    [Fact]
    public void Build_PostsToWindowOpenerWithTheExpectedMessageSource()
    {
        var html = OAuthPostMessagePage.Build(new { ok = true });

        Assert.Contains("window.opener.postMessage", html);
        Assert.Contains(OAuthPostMessagePage.MessageSource, html);
    }

    private static string ExtractAttribute(string html, string name)
    {
        var marker = $"{name}=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = html.IndexOf('"', start);
        return html[start..end];
    }

    private static int CountOccurrences(string haystack, string needle, StringComparison comparison)
    {
        var count = 0;
        var index = 0;
        while ((index = haystack.IndexOf(needle, index, comparison)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
