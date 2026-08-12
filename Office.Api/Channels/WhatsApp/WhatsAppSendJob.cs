using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Realtime;

namespace Office.Api.Channels.WhatsApp;

/// <summary>
/// Hangfire-и таъхиршуда (item 5) — POST /conversations/{id}/messages як паёми Pending
/// сабт мекунад ва ин job-ро бо таъхир (пешфарз 45с) schedule мекунад, то operator фурсати
/// бекор кардан дошта бошад. WhatsApp на edit дорад, на delete баъд аз он ки паём ба Meta
/// расид — пас ин ЯГОНА роҳи додани "second chance" аст.
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300, 1800])]
public class WhatsAppSendJob(
    AppDbContext db,
    IChannelProviderFactory factory,
    IInboxEventPublisher events,
    ILogger<WhatsAppSendJob> logger)
{
    public async Task SendAsync(Guid messageId, CancellationToken ct)
    {
        var message = await db.Messages
            .Include(m => m.Conversation).ThenInclude(c => c.Channel)
            .FirstOrDefaultAsync(m => m.Id == messageId, ct);

        if (message is null)
        {
            logger.LogWarning("WhatsAppSendJob: паёми {MessageId} ёфт нашуд.", messageId);
            return;
        }

        // CancelMessageAsync метавонад дар ҳамин лаҳза (race) паёмро Cancelled карда бошад —
        // танҳо агар то ҳол Pending бошад давом медиҳем. Ин ҳамон "cancel баъд аз dispatch
        // бояд самимона ноком шавад" - ро аз тарафи дигар таъмин мекунад: агар мо аллакай
        // ин ҷо бошем, cancel дигар муваффақ намешавад (ExecuteUpdateAsync-и он 0 мегардонад).
        if (message.DeliveryStatus != MessageDeliveryStatus.Pending)
        {
            logger.LogInformation(
                "WhatsAppSendJob: паёми {MessageId} дигар Pending нест ({Status}) — бекор карда шудааст, сарфи назар.",
                messageId, message.DeliveryStatus);
            return;
        }

        var conversation = message.Conversation;
        var channel = conversation.Channel;
        var isTemplate = message.TemplateName is not null;

        if (ConversationWindowCalculator.IsWindowClosed(isTemplate, conversation.WindowExpiresAt, DateTimeOffset.UtcNow))
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = "Тирезаи 24-соата дар давоми таъхир баста шуд — танҳо шаблон фиристода мешавад.";
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            return;
        }

        var provider = factory.GetProvider(channel.Type);

        try
        {
            message.ExternalId = isTemplate
                ? await provider.SendTemplateAsync(
                    channel, conversation.ExternalId, message.TemplateName!,
                    message.TemplateLanguage ?? "en_US", DeserializeParameters(message.TemplateParametersJson), ct)
                : await provider.SendMessageAsync(channel, conversation.ExternalId, message.Body ?? string.Empty, ct);

            message.DeliveryStatus = MessageDeliveryStatus.Sent;
            conversation.LastMessageAt = message.CreatedAt;
        }
        catch (WhatsAppWindowClosedException ex)
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = ex.Message;
            logger.LogWarning(ex, "WhatsAppSendJob: тирезаи 24-соата баста барои паёми {MessageId}.", messageId);
        }

        await db.SaveChangesAsync(ct);
        await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
    }

    private static IReadOnlyList<string> DeserializeParameters(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<IReadOnlyList<string>>(json) ?? [];

    private Task PublishAsync(Guid channelId, Guid? assignedTo, Message message, CancellationToken ct) =>
        events.MessageSentAsync(channelId, assignedTo, MessageDto.FromEntity(message), ct);
}
