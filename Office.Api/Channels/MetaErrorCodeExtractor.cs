using System.Text.Json;
using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>
/// Messages.FailureCode — шакли "{PROVIDER}_{code}[_{subcode}]" (масалан IG_2, WA_131030,
/// FB_100_2018074) аз ҷавоби хатои Meta. Ҳадаф: `fbtrace_id` дар ҳар дархост ЯГОНА аст (GROUP
/// BY бар он бефоида — ҳар хато "гурӯҳи худро" месозад), вале code/error_subcode такрор
/// мешаванд — воқеан якчанд навъи маҳдуди мушкил, на даҳҳо гурӯҳи алоҳида. Pure, ҳеҷ гоҳ
/// истисно намепартояд — ҷавоби ношинос/вайрон танҳо null медиҳад.
/// </summary>
public static class MetaErrorCodeExtractor
{
    public static string? Extract(ChannelType channelType, string? rawErrorResponse)
    {
        var prefix = ProviderPrefix(channelType);
        if (prefix is null || string.IsNullOrWhiteSpace(rawErrorResponse))
            return null;

        // Пеш аз FailureDetail (2026-08-25), FailureReason баъзан "{Provider} Graph API
        // хатогӣ: {json-и хом}" буд — на JSON-и холис. То backfill-и сатрҳои кӯҳна низ кор
        // кунад (ниг. миграция), аввалин "{"-ро меёбем ва аз он ҷо parse мекунем; барои JSON-и
        // аллакай холис ин ҳеҷ фарқе намекунад (индекси 0).
        var jsonStart = rawErrorResponse.IndexOf('{');
        if (jsonStart < 0)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(rawErrorResponse[jsonStart..]);
            if (!doc.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
                return null;

            if (!error.TryGetProperty("code", out var codeEl) || codeEl.ValueKind != JsonValueKind.Number)
                return null;

            var code = codeEl.GetInt32();
            var subcode = error.TryGetProperty("error_subcode", out var subcodeEl) && subcodeEl.ValueKind == JsonValueKind.Number
                ? subcodeEl.GetInt32()
                : (int?)null;

            return subcode is null ? $"{prefix}_{code}" : $"{prefix}_{code}_{subcode}";
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ProviderPrefix(ChannelType channelType) => channelType switch
    {
        ChannelType.WhatsApp => "WA",
        ChannelType.Instagram => "IG",
        ChannelType.Facebook => "FB",
        _ => null,
    };
}
