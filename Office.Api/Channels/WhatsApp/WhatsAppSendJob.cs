using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels;
using Office.Api.Channels.Messenger;
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

        // Садди охирин: ёддошти дохилӣ ҳеҷ гоҳ набояд ин ҷо расад (SendMessageAsync ҳеҷ гоҳ
        // барои он ин job-ро enqueue намекунад), вале агар бо роҳи дигар/хатогӣ расид ҳам,
        // ин ҷо — воқеан ҷои даъвати провайдер — қатъиян манъ мекунад, на танҳо дар endpoint.
        if (!InternalNoteGuard.CanDispatchToProvider(message.IsInternalNote))
        {
            logger.LogError(
                "WhatsAppSendJob: паёми {MessageId} ёддошти дохилӣ аст — набояд ин ҷо мерасид. Ба провайдер намерасонам.",
                messageId);
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
        var provider = factory.GetProvider(channel.Type);

        // Facebook/Instagram надоранд шаблон — HUMAN_AGENT-и тег ба ҷои он тирезаро то 7 рӯз
        // дароз мекунад (ниг. MessengerSendModePlanner). Ин навбати WhatsApp-ро (поён, тағйирнаёфта)
        // такрор намекунад, чунки IsWindowClosed(isTemplate,...) барои "тег" бетаваҷҷуҳ аст —
        // ҳамеша татбиқ мешавад, ҳеҷ гоҳ рад намекунад (Reject танҳо баъд аз 7 рӯз).
        if (channel.Type is ChannelType.Facebook or ChannelType.Instagram)
        {
            await SendMessengerAsync(provider, channel, conversation, message, ct);
            return;
        }

        var isTemplate = message.TemplateName is not null;

        if (ConversationWindowCalculator.IsWindowClosed(isTemplate, conversation.WindowExpiresAt, DateTimeOffset.UtcNow))
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = "Тирезаи 24-соата дар давоми таъхир баста шуд — танҳо шаблон фиристода мешавад.";
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            return;
        }

        try
        {
            message.ExternalId = isTemplate
                ? await provider.SendTemplateAsync(
                    channel, conversation.ExternalId, message.TemplateName!,
                    message.TemplateLanguage ?? "en_US", DeserializeParameters(message.TemplateParametersJson), ct)
                : await provider.SendMessageAsync(channel, conversation.ExternalId, message.Body ?? string.Empty, messageTag: null, ct);

            message.DeliveryStatus = MessageDeliveryStatus.Sent;
            conversation.LastMessageAt = message.CreatedAt;
        }
        catch (WhatsAppWindowClosedException ex)
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = ex.Message;
            logger.LogWarning(ex, "WhatsAppSendJob: тирезаи 24-соата баста барои паёми {MessageId}.", messageId);
        }
        catch (GraphApiException ex)
        {
            // ex.Message аллакай тарҷумашудааст (MetaErrorTranslator, дар провайдер) — на JSON-и
            // хом (ки пеш аз ин мустақим дар ҳубоб чоп мешуд ва тредро уфуқӣ мегардонд).
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = ex.Message;
            message.FailureDetail = ex.RawResponseBody.Length > 4000 ? ex.RawResponseBody[..4000] : ex.RawResponseBody;
            message.FailureCode = MetaErrorCodeExtractor.Extract(channel.Type, ex.RawResponseBody);
            // А2: коди ношинос — то дафъаи оянда маълумот дошта бошем (на боз "як маротиба").
            if (message.FailureCode is not null && !FailureCodeLabels.IsKnown(message.FailureCode))
                logger.LogWarning("WhatsAppSendJob: failure_code ношинос {FailureCode} — payload: {RawResponseBody}", message.FailureCode, ex.RawResponseBody);
            logger.LogError(ex, "WhatsAppSendJob: Meta Graph API рад кард — паёми {MessageId}.", messageId);
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            throw;
        }
        catch (Exception ex)
        {
            // Пеш аз ин ин ҷо ҳеҷ catch набуд — хатои умумӣ (масалан шабака) хомӯшона Hangfire-ро
            // такрор мекард, бе ҳеҷ FailureReason-е дар паём: оператор "абадан Pending" медид, бе
            // ҳеҷ нишонае, ки чизе вайрон шуд.
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            logger.LogError(ex, "WhatsAppSendJob: кӯшиши фиристодани паёми {MessageId} ноком шуд.", messageId);
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            throw;
        }

        await db.SaveChangesAsync(ct);
        await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
    }

    private async Task SendMessengerAsync(IChannelProvider provider, Channel channel, Conversation conversation, Message message, CancellationToken ct)
    {
        var mode = MessengerSendModePlanner.Plan(conversation.WindowExpiresAt, DateTimeOffset.UtcNow);

        if (mode == MessengerSendMode.Reject)
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = "Тирезаи 24-соат ва дарозкунии 7-рӯзаи тег (HUMAN_AGENT) ҳарду гузаштаанд.";
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            return;
        }

        var messageTag = mode == MessengerSendMode.Tag ? MessengerTags.HumanAgent : null;

        try
        {
            message.ExternalId = await provider.SendMessageAsync(channel, conversation.ExternalId, message.Body ?? string.Empty, messageTag, ct);
            message.DeliveryStatus = MessageDeliveryStatus.Sent;
            conversation.LastMessageAt = message.CreatedAt;
        }
        catch (GraphApiException ex)
        {
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = ex.Message;
            message.FailureDetail = ex.RawResponseBody.Length > 4000 ? ex.RawResponseBody[..4000] : ex.RawResponseBody;
            message.FailureCode = MetaErrorCodeExtractor.Extract(channel.Type, ex.RawResponseBody);
            // А2: коди ношинос — то дафъаи оянда маълумот дошта бошем (на боз "як маротиба").
            if (message.FailureCode is not null && !FailureCodeLabels.IsKnown(message.FailureCode))
                logger.LogWarning("WhatsAppSendJob: failure_code ношинос {FailureCode} — payload: {RawResponseBody}", message.FailureCode, ex.RawResponseBody);
            logger.LogError(ex, "WhatsAppSendJob: Meta Graph API рад кард (messenger) — паёми {MessageId}.", message.Id);
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            throw;
        }
        catch (Exception ex)
        {
            // Пеш аз ин ин ҷо (Instagram/Facebook-и матн) ҳеҷ catch набуд — исботшуда: Instagram
            // ҳозир message_attachments-и media-ро тамоман рад мекунад (500, то App Review), вале
            // ин роҳи МАТН аст, на media — гап дар бораи ҳар хатои дигари шабака/Meta.
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            message.FailureReason = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message;
            logger.LogError(ex, "WhatsAppSendJob: кӯшиши фиристодани паёми messenger {MessageId} ноком шуд.", message.Id);
            await db.SaveChangesAsync(ct);
            await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
            throw;
        }

        await db.SaveChangesAsync(ct);
        await PublishAsync(channel.Id, conversation.AssignedTo, message, ct);
    }

    private static IReadOnlyList<string> DeserializeParameters(string? json) =>
        json is null ? [] : JsonSerializer.Deserialize<IReadOnlyList<string>>(json) ?? [];

    private Task PublishAsync(Guid channelId, Guid? assignedTo, Message message, CancellationToken ct) =>
        events.MessageSentAsync(channelId, assignedTo, MessageDto.FromEntity(message), ct);
}
