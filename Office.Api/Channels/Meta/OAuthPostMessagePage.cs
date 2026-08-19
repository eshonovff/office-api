using System.Net;
using System.Text.Json;

namespace Office.Api.Channels.Meta;

/// <summary>
/// The Meta OAuth callback (Features/Channels/ChannelOAuthEndpoints.cs's CallbackAsync)
/// is a plain browser redirect Meta lands the bare browser on. When the SPA opens that
/// dance in a popup, there's no shared origin to rely on for reading the result back out
/// of it directly — so instead this renders a tiny self-closing page that posts the
/// result back via <c>window.postMessage</c>, the standard OAuth-popup pattern. That
/// works regardless of what origin the SPA itself is served from, dev tunnel or prod.
///
/// The payload is embedded as an HTML-encoded attribute rather than interpolated
/// straight into the inline &lt;script&gt; — a value that came from Meta (an account
/// name) could otherwise contain a literal "&lt;/script&gt;" and prematurely close the
/// tag. HTML-attribute encoding leaves no unescaped '&lt;' for the HTML parser to act
/// on, so there's nothing to break out with.
/// </summary>
public static class OAuthPostMessagePage
{
    public const string MessageSource = "office-oauth-callback";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// `payload` is posted to the opener as-is (an OAuthCallbackResponse on success, or a
    /// `{title, detail}` shape mirroring ProblemDetails on failure) — the caller on the
    /// SPA side distinguishes the two by shape, the same way it already would have for a
    /// plain JSON response.
    /// </summary>
    public static string Build(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var encoded = WebUtility.HtmlEncode(json);

        return $$"""
            <!doctype html>
            <html><head><meta charset="utf-8"></head>
            <body data-oauth-payload="{{encoded}}">
            <script>
              (function () {
                var payload = JSON.parse(document.body.getAttribute('data-oauth-payload'));
                if (window.opener) {
                  window.opener.postMessage({ source: '{{MessageSource}}', payload: payload }, '*');
                }
                window.close();
              })();
            </script>
            </body></html>
            """;
    }
}
