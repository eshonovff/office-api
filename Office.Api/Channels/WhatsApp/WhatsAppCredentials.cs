using System.Text.Json;

namespace Office.Api.Channels.WhatsApp;

/// <summary>
/// Шакли JSON-и credentials-и канали WhatsApp (пеш аз шифр дар <see cref="Data.Entities.Channel.CredentialsEncrypted"/>):
/// { "phoneNumberId": "...", "wabaId": "...", "accessToken": "..." }
/// </summary>
public record WhatsAppCredentials(string PhoneNumberId, string WabaId, string AccessToken)
{
    public static WhatsAppCredentials Parse(string json) =>
        JsonSerializer.Deserialize<WhatsAppCredentials>(json, JsonOptions)
        ?? throw new InvalidOperationException("Credentials-и WhatsApp хонда нашуд.");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
