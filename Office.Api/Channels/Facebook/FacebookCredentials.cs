using System.Text.Json;

namespace Office.Api.Channels.Facebook;

/// <summary>
/// Шакли JSON-и credentials-и канали Facebook (пеш аз шифр дар
/// <see cref="Data.Entities.Channel.CredentialsEncrypted"/>): { "pageId": "...", "pageAccessToken": "..." }
/// </summary>
public record FacebookCredentials(string PageId, string PageAccessToken)
{
    public static FacebookCredentials Parse(string json) =>
        JsonSerializer.Deserialize<FacebookCredentials>(json, JsonOptions)
        ?? throw new InvalidOperationException("Credentials-и Facebook хонда нашуд.");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
