using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.WhatsApp;

/// <summary>
/// Hangfire job барои фиристодани паём тавассути провайдер (вазифаи 5.13) — 3 маротиба
/// такрор, бо фосилаи афзоянда. Пеш аз ин, `Message`-и `Pending` бояд аллакай сабт шуда бошад
/// (фазаи 6-ро мебояд, ки ин job-ро баъди сабти паёми содиротӣ enqueue кунад).
/// </summary>
[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300, 1800])]
public class WhatsAppSendJob(AppDbContext db, IChannelProviderFactory factory, ILogger<WhatsAppSendJob> logger)
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

        var channel = message.Conversation.Channel;
        var provider = factory.GetProvider(channel.Type);

        try
        {
            message.ExternalId = await provider.SendMessageAsync(channel, message.Conversation.ExternalId, message.Body ?? string.Empty, ct);
            message.DeliveryStatus = MessageDeliveryStatus.Sent;
            await db.SaveChangesAsync(ct);
        }
        catch (WhatsAppWindowClosedException ex)
        {
            // Такрор фоида надорад — тиреза боз намекушояд. Танҳо SendTemplateAsync кор мекунад.
            message.DeliveryStatus = MessageDeliveryStatus.Failed;
            await db.SaveChangesAsync(ct);
            logger.LogWarning(ex, "WhatsAppSendJob: тирезаи 24-соата баста барои паёми {MessageId}.", messageId);
        }
    }
}
