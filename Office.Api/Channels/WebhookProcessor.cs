using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels;

/// <summary>
/// Job-и Hangfire барои коркарди webhook-и сабтшуда: parse → идентификатсияи
/// канал → идентификатсияи conversation → идемпотентии паём → upsert.
/// </summary>
public class WebhookProcessor(AppDbContext db, IChannelProviderFactory factory, ILogger<WebhookProcessor> logger)
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

        if (!root.TryGetProperty("channelExternalId", out var channelExternalIdElement))
        {
            log.Error = "channelExternalId дар payload нест.";
            return;
        }

        var channelExternalId = channelExternalIdElement.GetString();
        var channel = await db.Channels
            .FirstOrDefaultAsync(c => c.Type == channelType && c.ExternalId == channelExternalId, ct);

        if (channel is null)
        {
            log.Error = $"Канали '{channelExternalId}' (навъи {channelType}) ёфт нашуд.";
            return;
        }

        var provider = factory.GetProvider(channelType);
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

        foreach (var group in newMessages.GroupBy(m => m.ConversationExternalId))
            await UpsertConversationWithMessagesAsync(channel, group.Key, group.ToList(), ct);
    }

    private async Task UpsertConversationWithMessagesAsync(
        Channel channel, string conversationExternalId, List<ParsedWebhookMessage> messages, CancellationToken ct)
    {
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
            db.Messages.Add(new Message
            {
                Id = Guid.CreateVersion7(),
                ConversationId = conversation.Id,
                Direction = parsed.Direction,
                Type = parsed.Type,
                Body = parsed.Body,
                MediaUrl = parsed.MediaUrl,
                ExternalId = parsed.MessageExternalId,
                DeliveryStatus = parsed.Direction == MessageDirection.Inbound
                    ? MessageDeliveryStatus.Delivered
                    : MessageDeliveryStatus.Sent,
                CreatedAt = parsed.SentAt,
            });

            if (parsed.Direction == MessageDirection.Inbound)
                conversation.UnreadCount += 1;
        }

        var lastMessageAt = messages.Max(m => m.SentAt);
        if (conversation.LastMessageAt is null || lastMessageAt > conversation.LastMessageAt)
            conversation.LastMessageAt = lastMessageAt;

        // Тирезаи 24-соатаи посух — пешфарзи умумӣ, ки провайдерҳо дар фазаи 5/7 мувофиқи қоидаи худ дақиқ мекунанд.
        conversation.WindowExpiresAt = lastMessageAt.AddHours(24);
    }
}
