using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
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

        using var document = JsonDocument.Parse(log.RawJson);
        var root = document.RootElement;

        var provider = factory.GetProvider(channelType);
        var channelExternalId = provider.ExtractChannelExternalId(root);
        if (channelExternalId is null)
        {
            log.Error = "Идентификатсияи канал аз payload баромада натавонист.";
            return;
        }

        var channel = await db.Channels
            .FirstOrDefaultAsync(c => c.Type == channelType && c.ExternalId == channelExternalId, ct);

        if (channel is null)
        {
            log.Error = $"Канали '{channelExternalId}' (навъи {channelType}) ёфт нашуд.";
            return;
        }

        await ProcessNewMessagesAsync(channel, provider, root, ct);
        await ProcessStatusUpdatesAsync(channel, provider, root, ct);
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
            savedMessages.AddRange(await UpsertConversationWithMessagesAsync(channel, group.Key, group.ToList(), ct));

        await db.SaveChangesAsync(ct);

        foreach (var (message, conversation, mediaExternalId) in savedMessages)
        {
            await events.MessageReceivedAsync(channel.Id, conversation.AssignedTo, MessageDto.FromEntity(message), ct);

            if (mediaExternalId is not null)
                backgroundJobs.Enqueue<MediaDownloadJob>(j => j.DownloadAsync(message.Id, mediaExternalId, CancellationToken.None));
        }
    }

    private async Task ProcessStatusUpdatesAsync(Channel channel, IChannelProvider provider, JsonElement root, CancellationToken ct)
    {
        var updates = await provider.ParseStatusUpdatesAsync(channel, root, ct);
        if (updates.Count == 0)
            return;

        var externalIds = updates.Select(u => u.MessageExternalId).ToList();
        var messages = await db.Messages
            .Include(m => m.Conversation)
            .Include(m => m.SentByUser)
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
        Channel channel, string conversationExternalId, List<ParsedWebhookMessage> messages, CancellationToken ct)
    {
        var savedMessages = new List<(Message, Conversation, string?)>();

        var conversation = await db.Conversations
            .FirstOrDefaultAsync(c => c.ChannelId == channel.Id && c.ExternalId == conversationExternalId, ct);

        if (conversation is null)
        {
            conversation = new Conversation
            {
                Id = Guid.CreateVersion7(),
                ChannelId = channel.Id,
                ExternalId = conversationExternalId,
                ContactName = messages[0].ContactName,
                ContactAvatarUrl = messages[0].ContactAvatarUrl,
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
