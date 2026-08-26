using System.Text.Json;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Instagram;

/// <summary>
/// Табдили raw JSON-и webhook-и Instagram Messaging ба шаклҳои нормализатсияшуда — pure,
/// бе DB/HTTP. Шакли payload: <c>{"object":"instagram","entry":[{"id":"&lt;IG_ID&gt;",
/// "messaging":[{"sender":{"id":...},"message":{"mid":...,"text":...}}]}]}</c> — ҳамон
/// <c>entry[].messaging[]</c>-и Facebook (на <c>entry[].changes[]</c>-и WhatsApp), чунки
/// Instagram Messaging ба ҳамон Messenger Platform асос ёфтааст.
///
/// Ҳеҷ навъи паём хомӯшона партофта намешавад: attachment-и ношинос → MessageType.Text бо
/// матни <see cref="UnsupportedTypeBodyPrefix"/> (InstagramProvider ин ҳолатро log мекунад).
/// </summary>
public static class InstagramPayloadParser
{
    /// <summary>Пешвои санадест, ки ин рекорд аз навъи "unsupported"-и ин парсер аст — InstagramProvider инро log мекунад.</summary>
    public const string UnsupportedTypeBodyPrefix = "[навъи дастгирӣнашуда: ";

    public static string? ExtractChannelExternalId(JsonElement payload) =>
        payload.TryGetProperty("entry", out var entryEl) && entryEl.ValueKind == JsonValueKind.Array && entryEl.GetArrayLength() > 0 &&
        entryEl[0].TryGetProperty("id", out var idEl)
            ? idEl.GetString()
            : null;

    /// <summary>Response-и `POST /{ig-id}/messages`: <c>{"recipient_id":"...","message_id":"..."}</c> (шакли Facebook-монанд).</summary>
    public static string? ExtractSentMessageId(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement.TryGetProperty("message_id", out var idEl) ? idEl.GetString() : null;
    }

    public static IReadOnlyList<ParsedWebhookMessage> ParseMessages(JsonElement payload)
    {
        var result = new List<ParsedWebhookMessage>();

        foreach (var messagingEvent in EnumerateMessagingEvents(payload))
        {
            var senderId = messagingEvent.GetProperty("sender").GetProperty("id").GetString()!;
            var timestampMs = messagingEvent.GetProperty("timestamp").GetInt64();
            var sentAt = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs);

            if (messagingEvent.TryGetProperty("message", out var messageEl))
            {
                // Эхои паёме, ки худи мо мустақим аз барномаи Instagram фиристодаем (на тавассути
                // ин платформа) — Meta онро низ ҳамчун webhook мефиристад (is_echo=true). Бояд
                // ҳамчун Outbound сабт шавад, на партофта: вагарна дар UI гум мешавад. sender дар
                // ин ҳолат худи аккаунти IG-и мо аст, на мижоз — пас чат бояд ба recipient (мижоз)
                // алоқаманд шавад, вагарна чати такрорӣ бо ID-и худи аккаунти мо сохта мешавад.
                var isEcho = messageEl.TryGetProperty("is_echo", out var echoEl) && echoEl.ValueKind == JsonValueKind.True;
                var conversationExternalId = isEcho
                    ? messagingEvent.GetProperty("recipient").GetProperty("id").GetString()!
                    : senderId;

                var mid = messageEl.GetProperty("mid").GetString()!;
                var (type, body, mediaUrl, externalContentUrl, externalContentKind) = MapMessageContent(messageEl);

                result.Add(new ParsedWebhookMessage(
                    ConversationExternalId: conversationExternalId,
                    ContactName: null,
                    ContactAvatarUrl: null,
                    MessageExternalId: mid,
                    // sent_by_user_id холӣ мемонад (агенти мо не буд) — UI бо Direction/пуррагии он
                    // "аз Instagram" нишон медиҳад, ниг. MessageBubble.tsx.
                    Direction: isEcho ? MessageDirection.Outbound : MessageDirection.Inbound,
                    Type: type,
                    Body: body,
                    MediaUrl: null,
                    SentAt: sentAt,
                    // Ҳамон алгуи Facebook: URL-и CDN бо мӯҳлат, на media id — InstagramProvider.
                    // DownloadMediaAsync онро мустақим GET мекунад.
                    MediaExternalId: mediaUrl,
                    ExternalContentUrl: externalContentUrl,
                    ExternalContentKind: externalContentKind));

                continue;
            }

            if (messagingEvent.TryGetProperty("reaction", out var reactionEl))
                result.Add(ParseReaction(senderId, timestampMs, sentAt, reactionEl));
        }

        return result;
    }

    private static ParsedWebhookMessage ParseReaction(string senderId, long timestampMs, DateTimeOffset sentAt, JsonElement reactionEl)
    {
        // Реаксия (double-tap/emoji ба паёми қаблӣ) — на паёми нав дар маънои муқаррарӣ, вале
        // ҳеҷ гоҳ набояд хомӯшона гум шавад. reaction.mid ба паёми РЕАКСИЯШУДА ишора мекунад
        // (на ин рӯйдод), пас ба он алоқаманд намекунем — синтетикӣ id месозем (ниг. postback-и Facebook).
        var action = reactionEl.TryGetProperty("action", out var actionEl) ? actionEl.GetString() : "react";
        var emoji = reactionEl.TryGetProperty("emoji", out var emojiEl) ? emojiEl.GetString()
            : reactionEl.TryGetProperty("reaction", out var reactionNameEl) ? reactionNameEl.GetString() : null;

        var body = action == "unreact"
            ? "[реаксия бардошта шуд]"
            : $"[реаксия: {emoji ?? "?"}]";

        return new ParsedWebhookMessage(
            ConversationExternalId: senderId,
            ContactName: null,
            ContactAvatarUrl: null,
            MessageExternalId: $"reaction:{senderId}:{timestampMs}",
            Direction: MessageDirection.Inbound,
            Type: MessageType.Text,
            Body: body,
            MediaUrl: null,
            SentAt: sentAt);
    }

    /// <summary>
    /// delivery.mids — ҳамон шакли Facebook, агар Instagram ин навъи event-ро фиристад (дар
    /// вақти навишта шудани ин парсер бо санҷиши зинда тасдиқ нашудааст — ниг. report). "read"
    /// (watermark, на per-message) ба монанди Facebook партофта мешавад.
    /// </summary>
    public static IReadOnlyList<ParsedStatusUpdate> ParseStatusUpdates(JsonElement payload)
    {
        var result = new List<ParsedStatusUpdate>();

        foreach (var messagingEvent in EnumerateMessagingEvents(payload))
        {
            if (!messagingEvent.TryGetProperty("delivery", out var deliveryEl) ||
                !deliveryEl.TryGetProperty("mids", out var midsEl) || midsEl.ValueKind != JsonValueKind.Array)
                continue;

            var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(messagingEvent.GetProperty("timestamp").GetInt64());

            foreach (var midEl in midsEl.EnumerateArray())
            {
                var mid = midEl.GetString();
                if (mid is not null)
                    result.Add(new ParsedStatusUpdate(mid, MessageDeliveryStatus.Delivered, updatedAt));
            }
        }

        return result;
    }

    private static IEnumerable<JsonElement> EnumerateMessagingEvents(JsonElement payload)
    {
        if (!payload.TryGetProperty("entry", out var entryEl) || entryEl.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var entry in entryEl.EnumerateArray())
        {
            if (!entry.TryGetProperty("messaging", out var messagingEl) || messagingEl.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var evt in messagingEl.EnumerateArray())
                yield return evt;
        }
    }

    private static (MessageType Type, string? Body, string? MediaUrl, string? ExternalContentUrl, string? ExternalContentKind) MapMessageContent(
        JsonElement messageEl)
    {
        var text = messageEl.TryGetProperty("text", out var textEl) ? textEl.GetString() : null;

        // Ҷавоб ба сторис: паёми матнии оддӣ + reply_to.story. URL-и сторис ин ҷо ҳам (ниг.
        // ig_reel/ig_post поён барои далел) эҳтимолан пайванди веб аст, на CDN — санҷиши зиндаи
        // мустақим барои ҳамин ҳолат карда нашудааст (сторис 24 соат зинда аст, дидани воқеӣ дар
        // production душвор), вале сохтори якхела (ҳамон Messenger Platform "мубодилаи мазмуни
        // берунӣ") бо эҳтиёт ҳамин тавр рафтор мекунад: MediaExternalId не, ExternalContentUrl бале.
        if (messageEl.TryGetProperty("reply_to", out var replyToEl) && replyToEl.TryGetProperty("story", out var storyEl))
        {
            var storyUrl = storyEl.TryGetProperty("url", out var storyUrlEl) ? storyUrlEl.GetString() : null;
            return (MessageType.StoryReply, text, null, storyUrl, storyUrl is null ? null : "Story");
        }

        if (!messageEl.TryGetProperty("attachments", out var attachmentsEl) || attachmentsEl.ValueKind != JsonValueKind.Array ||
            attachmentsEl.GetArrayLength() == 0)
        {
            return (MessageType.Text, text, null, null, null);
        }

        var attachment = attachmentsEl[0];
        var attachmentType = attachment.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
        var payloadEl = attachment.TryGetProperty("payload", out var pEl) ? pEl : default;
        var url = payloadEl.ValueKind == JsonValueKind.Object && payloadEl.TryGetProperty("url", out var urlEl) ? urlEl.GetString() : null;

        switch (attachmentType)
        {
            case "image":
                return (MessageType.Image, text, url, null, null);
            case "video":
                return (MessageType.Video, text, url, null, null);
            case "audio":
                return (MessageType.Audio, text, url, null, null);
            case "file":
                return (MessageType.File, text, url, null, null);

            // story_mention: корбар account-ро дар сторисаш зикр кард (на "reply" — attachment-и
            // алоҳида, бе reply_to). Ҳамон эҳтиёти боло (ниг. reply_to.story).
            case "story_mention":
                return (MessageType.StoryReply, text, null, url, url is null ? null : "Story");

            // Story-и мубодилашуда (муштарӣ story-ро фиристод — на story_mention/reply_to.story,
            // ки дигаранд, шакли payload комилан фарқ мекунад). Тасдиқшуда бо webhook_logs-и
            // ВОҚЕИИ production (2026-08-25, ниг. report): майдонҳо story_media_id/story_media_url
            // ҳастанд, на url/title (унвон умуман нест). story_media_url ин ҷо URL-и ВОҚЕИИ CDN аст
            // (lookaside.fbsbx.com/ig_messaging_cdn/..., ҳамон шакли ig_post) — пас MediaExternalId
            // пур мешавад, медиа тавассути роҳи муқаррарии MediaDownloadJob зеркашӣ мешавад.
            case "ig_story":
            {
                var storyMediaUrl = payloadEl.ValueKind == JsonValueKind.Object &&
                    payloadEl.TryGetProperty("story_media_url", out var storyMediaUrlEl)
                    ? storyMediaUrlEl.GetString()
                    : null;
                return (MessageType.Video, "[Story]", storyMediaUrl, storyMediaUrl, storyMediaUrl is null ? null : "Story");
            }

            // Reel-и мубодилашуда: MessageType-и ҷудогона надорем — video бо нишонаи "[Reel]" дар
            // матн (танҳо унвон, БЕ URL дар матн — ExternalContentUrl майдони алоҳида барои
            // "Кушодан дар Instagram"-и frontend, на URL дар матни паём).
            //
            // МУҲИМ (тасдиқшуда 2026-08-24 бо санҷиши зиндаи production, на тахмин — ниг. report):
            // payload.url барои ig_reel ПАЙВАНДИ САҲИФАИ ВЕБ аст (масалан instagram.com/reel/<code>/),
            // на URL-и CDN-и медиаи хом (curl бо User-Agent-и воқеӣ HTML-и саҳифаро баргардонд, на
            // видео). MediaExternalId қасдан NULL аст — MediaDownloadJob ҳеҷ гоҳ ба ин URL муваффақ
            // намешавад.
            case "ig_reel":
            {
                var title = payloadEl.ValueKind == JsonValueKind.Object && payloadEl.TryGetProperty("title", out var titleEl)
                    ? titleEl.GetString()
                    : null;
                var caption = string.IsNullOrEmpty(title) ? "[Reel]" : $"[Reel] {title}";
                return (MessageType.Video, caption, null, url, url is null ? null : "Reel");
            }

            // Пости мубодилашуда: БАРХИЛОФИ ig_reel — тасдиқшуда бо санҷиши зиндаи production
            // (2026-08-25, ниг. report): payload.url ин ҷо URL-и ВОҚЕИИ CDN аст (lookaside.fbsbx.com/
            // ig_messaging_cdn/..., ҳамон шакле ки барои attachment-ҳои муқаррарии сурат/видео/овоз
            // истифода мешавад), на пайванди веб. Бинобар ин, БАРХИЛОФИ ислоҳи қаблӣ, MediaExternalId
            // пур мешавад — медиа тавассути роҳи муқаррарии MediaDownloadJob зеркашӣ мешавад.
            // ExternalContentUrl/Kind ҳам якҷоя пур мешаванд, то фронтенд илова бар плеер тугмаи
            // "Кушодан дар Instagram"-ро низ пешниҳод кунад (пости аслиро дидан).
            case "ig_post":
            {
                var title = payloadEl.ValueKind == JsonValueKind.Object && payloadEl.TryGetProperty("title", out var titleEl)
                    ? titleEl.GetString()
                    : null;
                var caption = string.IsNullOrEmpty(title) ? "[Post]" : $"[Post] {title}";
                // Тасдиқшуда бо 11 payload-и воқеии production (2026-08-20 то 2026-08-25, аз ҷумла
                // каруселҳои воқеӣ, ки муштарӣ такроран фиристодааст — ниг. report): message.
                // attachments ҳамеша якдона аст, ва payload ҳамеша якхела — {url, title,
                // ig_post_media_id}, ҳеҷ гоҳ рӯйхати элементҳо/сурат. Барои пости каруселӣ ҳам Meta
                // танҳо як url (сурати аввал/муқова) медиҳад — сурату видеоҳои дигари он пост
                // тавассути ин webhook дастнорас аст, роҳи дигар низ ёфт нашуд. Аз ин рӯ ваъдаи
                // "(+N боз)" бардошта шуд — чунин ваъда дуруғ мебуд.
                return (MessageType.Video, caption, url, url, url is null ? null : "Post");
            }

            // Стикери дил (double-tap/heart sticker) — расм/видео надорад, барои сабти "навъи
            // маълум" (на паёми холӣ) матни собит истифода мешавад.
            case "like_heart":
                return (MessageType.Text, "❤️ (стикер)", null, null, null);

            // Ҳеҷ навъ хомӯшона партофта намешавад — InstagramProvider.ParseWebhookAsync ин
            // ҳолатро log мекунад (UnsupportedTypeBodyPrefix-ро санҷида).
            default:
                return (MessageType.Text, $"{UnsupportedTypeBodyPrefix}{attachmentType ?? "(бе навъ)"}]", url, null, null);
        }
    }

    /// <summary>
    /// Танҳо барои ташхис (InstagramProvider инро log мекунад): JSON-и пурраи payload-и
    /// attachment-ҳои ig_reel/ig_post/ig_story/story_mention/reply_to.story. Ҳадаф: бидонем, оё
    /// Meta дар онҳо майдони preview/thumbnail (масалан thumbnail_url, image_url) мефиристад — то
    /// ҳол дида нашудааст (ниг. report), пас парсер аллакай онро истифода намекунад. Инчунин
    /// маҳз ҳамин лог буд, ки ig_story-ро (шакли комилан дигари payload) 2026-08-25 ошкор кард —
    /// пас ҳангоми навъи нав пайдо шудан низ ҳамин тавр кор мекунад.
    /// </summary>
    public static IReadOnlyList<string> ExtractExternalContentPayloadsForDiagnostics(JsonElement payload)
    {
        var result = new List<string>();

        foreach (var messagingEvent in EnumerateMessagingEvents(payload))
        {
            if (!messagingEvent.TryGetProperty("message", out var messageEl))
                continue;

            if (messageEl.TryGetProperty("reply_to", out var replyToEl) && replyToEl.TryGetProperty("story", out var storyEl))
                result.Add(storyEl.GetRawText());

            if (!messageEl.TryGetProperty("attachments", out var attachmentsEl) || attachmentsEl.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var attachment in attachmentsEl.EnumerateArray())
            {
                var type = attachment.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                if (type is "ig_reel" or "ig_post" or "ig_story" or "story_mention" && attachment.TryGetProperty("payload", out var pEl))
                    result.Add(pEl.GetRawText());
            }
        }

        return result;
    }
}
