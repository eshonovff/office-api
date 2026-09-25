using System.Text.Json;
using System.Text.Json.Nodes;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Automation;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Realtime;

namespace Office.Api.Channels;

/// <summary>
/// Job-и Hangfire барои коркарди webhook-и сабтшуда: parse → идентификатсияи
/// канал → идентификатсияи conversation → идемпотентии паём → upsert →
/// навсозии статус → enqueue-и боркунии media → огоҳии realtime.
/// </summary>
public class WebhookProcessor(
    AppDbContext db,
    IChannelProviderFactory factory,
    IInboxEventPublisher events,
    IBackgroundJobClient backgroundJobs,
    CommentAutomationProcessor commentAutomation,
    FlowTriggerProcessor flowTrigger,
    ILogger<WebhookProcessor> logger)
{
    public async Task ProcessAsync(Guid webhookLogId, CancellationToken ct)
    {
        var log = await db.WebhookLogs.FirstOrDefaultAsync(w => w.Id == webhookLogId, ct);
        if (log is null)
            return;

        try
        {
            await ProcessInternalAsync(log, ct);
            log.ProcessedAt = DateTimeOffset.UtcNow;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Webhook processing failed for log {WebhookLogId}", webhookLogId);
            log.Error = ex.Message;
            log.ProcessedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task ProcessInternalAsync(WebhookLog log, CancellationToken ct)
    {
        if (!Enum.TryParse<ChannelType>(log.Provider, ignoreCase: true, out var channelType))
        {
            log.Error = $"Провайдери номаълум: {log.Provider}";
            return;
        }

        var provider = factory.GetProvider(channelType);

        // Meta may batch the events of several accounts into one delivery — one entry[] item per
        // account. Every entry is processed on its own, against the channel IT names: handling the
        // whole batch under the first entry's channel would store one мизоҷ's messages or comments
        // in another мизоҷ's channel (see docs/phases/phase-16-comments.md).
        using var document = JsonDocument.Parse(log.RawJson);
        var errors = new List<string>();
        foreach (var entryPayload in SplitByEntry(document.RootElement))
        {
            using (entryPayload)
            {
                var error = await ProcessEntryAsync(entryPayload.RootElement, channelType, provider, ct);
                if (error is not null)
                    errors.Add(error);
            }
        }

        log.Error = errors.Count == 0 ? null : string.Join(" | ", errors);
    }

    /// <summary>
    /// The payload once per entry, each with only that entry (the parsers keep reading "entry[]"
    /// exactly as before). A payload of zero or one entry comes back as a single copy.
    /// </summary>
    internal static List<JsonDocument> SplitByEntry(JsonElement root)
    {
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() <= 1)
            return [JsonDocument.Parse(root.GetRawText())];

        var result = new List<JsonDocument>();
        foreach (var entry in entries.EnumerateArray())
        {
            var single = new JsonObject();
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name != "entry")
                    single[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }
            single["entry"] = new JsonArray(JsonNode.Parse(entry.GetRawText()));
            result.Add(JsonDocument.Parse(single.ToJsonString()));
        }

        return result;
    }

    /// <returns>An error for the webhook log, or null.</returns>
    private async Task<string?> ProcessEntryAsync(JsonElement root, ChannelType channelType, IChannelProvider provider, CancellationToken ct)
    {
        var channelExternalId = provider.ExtractChannelExternalId(root);
        if (channelExternalId is null)
            return "Идентификатсияи канал аз payload баромада натавонист.";

        var channel = await db.Channels
            .FirstOrDefaultAsync(c => c.Type == channelType && c.ExternalId == channelExternalId, ct);

        if (channel is null)
            return $"Канали '{channelExternalId}' (навъи {channelType}) ёфт нашуд.";

        // A мизоҷ disconnected this channel: its token is gone (CustomerChannelsEndpoints.Disconnect),
        // so nothing may run or be sent for it — Meta keeps delivering webhooks regardless.
        // Company channels keep their existing behaviour (see PROGRESS open issue on IsActive).
        if (channel.CustomerId is not null && !channel.IsActive)
            return $"Канали мизоҷ '{channelExternalId}' ҷудо карда шудааст — webhook коркард нашуд.";

        // Шакли коментарии Instagram (entry[].changes[], field="comments") бо шакли паёми
        // муқаррарӣ (entry[].messaging[]) комилан фарқ мекунад — InstagramPayloadParser.ParseMessages
        // онро намефаҳмад (ва бехатарона холӣ бармегардонад), пас шохаи ҷудогона лозим аст.
        var comments = channelType == ChannelType.Instagram ? InstagramPayloadParser.ParseCommentEvents(root) : [];
        if (comments.Count > 0)
        {
            // Every comment of the entry, not just the first — Meta batches them too.
            foreach (var commentEvent in comments)
            {
                await commentAutomation.ProcessAsync(channel, commentEvent, ct);
                // Паҳлӯи automation_rules-и Фазаи 10 (боло), на ба ҷои он — ниг. шарҳи FlowTriggerProcessor.
                await flowTrigger.ProcessCommentAsync(channel, commentEvent, ct);
            }
            return null;
        }

        await ProcessNewMessagesAsync(channel, provider, root, ct);
        await ProcessStatusUpdatesAsync(channel, provider, root, ct);
        return null;
    }

    private async Task ProcessNewMessagesAsync(Channel channel, IChannelProvider provider, JsonElement root, CancellationToken ct)
    {
        var incoming = await provider.ParseWebhookAsync(channel, root, ct);
        if (incoming.Count == 0)
            return;

        var incomingIds = incoming.Select(m => m.MessageExternalId).ToList();
        var existingIds = await db.Messages
            .Where(m => m.ExternalId != null && incomingIds.Contains(m.ExternalId))
            .Select(m => m.ExternalId!)
            .ToListAsync(ct);

        var newMessages = MessageIdempotencyPlanner.FilterNew(incoming, existingIds.ToHashSet());
        if (newMessages.Count == 0)
            return;

        var savedMessages = new List<(Message Message, Conversation Conversation, string? MediaExternalId)>();
        foreach (var group in newMessages.GroupBy(m => m.ConversationExternalId))
            savedMessages.AddRange(await UpsertConversationWithMessagesAsync(channel, provider, group.Key, group.ToList(), ct));

        await db.SaveChangesAsync(ct);

        var parsedByExternalId = newMessages.ToDictionary(m => m.MessageExternalId);
        foreach (var (message, conversation, mediaExternalId) in savedMessages)
        {
            await events.MessageReceivedAsync(channel.Id, conversation.AssignedTo, MessageDto.FromEntity(message), ct);

            if (mediaExternalId is not null)
                backgroundJobs.Enqueue<MediaDownloadJob>(j => j.DownloadAsync(message.Id, mediaExternalId, CancellationToken.None));

            // Фазаи 12: Flow Builder — паҳлӯи рӯйхати мавҷуда, ба ҷои он даст намезанад.
            if (message.ExternalId is not null && parsedByExternalId.TryGetValue(message.ExternalId, out var parsed))
                await flowTrigger.ProcessMessageAsync(channel, conversation, parsed, ct);
        }
    }

    private async Task ProcessStatusUpdatesAsync(Channel channel, IChannelProvider provider, JsonElement root, CancellationToken ct)
    {
        var updates = await provider.ParseStatusUpdatesAsync(channel, root, ct);
        if (updates.Count == 0)
            return;

        var externalIds = updates.Select(u => u.MessageExternalId).ToList();
        // SentByUserName is a persisted snapshot on Message itself — no need to
        // Include(SentByUser) just to resolve the display name for FromEntity below.
        var messages = await db.Messages
            .Include(m => m.Conversation)
            .Where(m => m.ExternalId != null && externalIds.Contains(m.ExternalId))
            .ToDictionaryAsync(m => m.ExternalId!, ct);

        var changedMessages = new List<Message>();
        foreach (var update in updates)
        {
            if (messages.TryGetValue(update.MessageExternalId, out var message) && update.Status > message.DeliveryStatus)
            {
                message.DeliveryStatus = update.Status;
                changedMessages.Add(message);
            }
        }

        if (changedMessages.Count == 0)
            return;

        await db.SaveChangesAsync(ct);

        // sent/delivered/read/failed — тамоми "тик"-ҳое, ки WhatsApp UI нишон медиҳад,
        // на танҳо delivered/read; коди зерин фарқ намекунад, пас ҳама якхела ирсол мешаванд.
        foreach (var message in changedMessages)
        {
            await events.MessageSentAsync(
                channel.Id, message.Conversation.AssignedTo, MessageDto.FromEntity(message), ct);
        }
    }

    private async Task<List<(Message Message, Conversation Conversation, string? MediaExternalId)>> UpsertConversationWithMessagesAsync(
        Channel channel, IChannelProvider provider, string conversationExternalId, List<ParsedWebhookMessage> messages, CancellationToken ct)
    {
        var savedMessages = new List<(Message, Conversation, string?)>();

        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.ChannelId == channel.Id && c.ExternalId == conversationExternalId, ct);

        if (conversation is null)
        {
            // WhatsApp номро дар худи webhook медиҳад (ParsedWebhookMessage.ContactName) — ин ҷо
            // ҳатто дархост намезанад (GetContactProfileAsync-и он ҳамеша Empty). Facebook/Instagram
            // намедиҳанд — як дархости алоҳида, танҳо як маротиба барои ҳамин мижоз (на барои
            // ҳар паём), ҳангоми сохтани conversation.
            var profile = messages[0].ContactName is null
                ? await provider.GetContactProfileAsync(channel, conversationExternalId, ct)
                : ContactProfile.Empty;

            conversation = new Conversation
            {
                Id = Guid.CreateVersion7(),
                ChannelId = channel.Id,
                ExternalId = conversationExternalId,
                ContactName = messages[0].ContactName ?? profile.Name,
                ContactAvatarUrl = messages[0].ContactAvatarUrl ?? profile.AvatarUrl,
                ContactUsername = profile.Username,
                // Танҳо вақте ки воқеан кӯшиш кардем (WhatsApp ҳеҷ гоҳ, чунки боло аллакай
                // ContactName дорад) — ниг. InstagramContactProfileBackfillJob барои сабаб.
                ContactProfileFetchedAt = messages[0].ContactName is null ? DateTimeOffset.UtcNow : null,
                Status = ConversationStatus.New,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Conversations.Add(conversation);
        }

        var latestInboundName = messages.LastOrDefault(m => m.ContactName is not null)?.ContactName;
        if (latestInboundName is not null)
            conversation.ContactName = latestInboundName;

        foreach (var parsed in messages.OrderBy(m => m.SentAt))
        {
            var message = new Message
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversation.Id,
                Direction = parsed.Direction,
                Type = parsed.Type,
                Body = parsed.Body,
                MediaUrl = parsed.MediaUrl,
                MimeType = parsed.MimeType,
                OriginalFileName = parsed.OriginalFileName,
                ExternalContentUrl = parsed.ExternalContentUrl,
                ExternalContentKind = parsed.ExternalContentKind,
                ExternalId = parsed.MessageExternalId,
                DeliveryStatus = parsed.Direction == MessageDirection.Inbound
                    ? MessageDeliveryStatus.Delivered
                    : MessageDeliveryStatus.Sent,
                CreatedAt = parsed.SentAt,
            };
            db.Messages.Add(message);
            savedMessages.Add((message, conversation, parsed.MediaExternalId));

            if (parsed.Direction == MessageDirection.Inbound)
                conversation.UnreadCount += 1;
        }

        var lastMessageAt = messages.Max(m => m.SentAt);
        if (conversation.LastMessageAt is null || lastMessageAt > conversation.LastMessageAt)
            conversation.LastMessageAt = lastMessageAt;

        conversation.WindowExpiresAt = ConversationWindowCalculator.ComputeExpiresAt(messages, conversation.WindowExpiresAt);

        return savedMessages;
    }
}
