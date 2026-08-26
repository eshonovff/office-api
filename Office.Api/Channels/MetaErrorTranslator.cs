using System.Text.Json;

namespace Office.Api.Channels;

/// <summary>
/// Ҷавоби хатои Meta Graph API — {"error":{"code":...,"is_transient":...,...}} — ба матни
/// кӯтоҳи инсонфаҳм табдил медиҳад, то раванди хоми JSON ҳеҷ гоҳ мустақим дар ҳубоби паём
/// чоп нашавад (санҷида: сатри бе фосида, тамоми треди чатро уфуқӣ мегардонд).
///
/// Рамзҳо/маъноҳо санҷида шуда 2026-08-25, аз ҳуҷҷатҳои расмии Meta (Graph API error codes,
/// WhatsApp Cloud API error codes reference) — Meta метавонад инҳоро тағйир диҳад, ин рӯйхат
/// бояд вақт-вақт аз нав санҷида шавад.
/// </summary>
public static class MetaErrorTranslator
{
    private const int RecipientNotInAllowedListCode = 131030;
    private const int TokenExpiredCode = 190;
    private const int WindowClosedCode = 131047;
    private const int WindowClosedAltCode = 470;
    private const int TransientServiceCode = 2;

    private const string RecipientNotAllowedMessage = "Рақами гиранда дар рӯйхати иҷозатдодашуда нест.";
    private const string TokenExpiredMessage = "Токен аз эътибор соқит шуд — каналро аз нав пайваст кунед.";
    private const string WindowClosedMessage = "Тирезаи 24-соата баста аст.";
    private const string TransientMessage = "Хидмати Meta муваққатан дастрас нест.";
    private const string GenericMessage = "Паём фиристода нашуд.";

    /// <summary>Ҳеҷ гоҳ null/холӣ бармегардонад — ҳатто payload-и ношинос ё вайрон ҳам GenericMessage мегирад.</summary>
    public static string Translate(string? rawResponseBody)
    {
        var parsed = TryParseError(rawResponseBody);
        if (parsed is null)
            return GenericMessage;

        var (code, isTransient) = parsed.Value;

        return code switch
        {
            RecipientNotInAllowedListCode => RecipientNotAllowedMessage,
            TokenExpiredCode => TokenExpiredMessage,
            WindowClosedCode or WindowClosedAltCode => WindowClosedMessage,
            TransientServiceCode => TransientMessage,
            _ => isTransient ? TransientMessage : GenericMessage,
        };
    }

    private static (int Code, bool IsTransient)? TryParseError(string? rawResponseBody)
    {
        if (string.IsNullOrWhiteSpace(rawResponseBody))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(rawResponseBody);
            if (!doc.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return null;

            var code = error.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number
                ? codeEl.GetInt32()
                : 0;
            var isTransient = error.TryGetProperty("is_transient", out var transientEl) && transientEl.ValueKind == JsonValueKind.True;

            return (code, isTransient);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
