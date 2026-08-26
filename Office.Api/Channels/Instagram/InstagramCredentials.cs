using System.Text.Json;

namespace Office.Api.Channels.Instagram;

/// <summary>
/// Шакли JSON-и credentials-и канали Instagram (пеш аз шифр дар
/// <see cref="Data.Entities.Channel.CredentialsEncrypted"/>): { "instagramAccountId": "...", "accessToken": "..." }
/// </summary>
public record InstagramCredentials(string InstagramAccountId, string AccessToken)
{
    public static InstagramCredentials Parse(string json) =>
        JsonSerializer.Deserialize<InstagramCredentials>(json, JsonOptions)
        ?? throw new InvalidOperationException("Credentials-и Instagram хонда нашуд.");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
