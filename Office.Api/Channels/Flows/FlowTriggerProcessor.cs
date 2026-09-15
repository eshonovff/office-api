using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Automation;
using Office.Api.Channels.Instagram;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Ёфтани flow-и мувофиқ ва сар кардани сессия — паҳлӯи CommentAutomationProcessor-и Фазаи 10
/// (навъи "simple"), НА ба ҷои он: ду система мустақиланд, ҳар кадом идемпотентии худро дорад
/// (ин синф бо FlowSession.TriggerExternalId, он бо AutomationRun.TriggerExternalId), пас як
/// коментарий/DM метавонад ҳам як automation_rule, ҳам як flow-ро ҳамзамон фаъол кунад.
/// </summary>
public class FlowTriggerProcessor(AppDbContext db, FlowEngine engine, ILogger<FlowTriggerProcessor> logger)
{
    private const string TriggerTypeComment = "instagram_comment";
    private const string TriggerTypeDm = "instagram_dm";

    public async Task ProcessCommentAsync(Channel channel, ParsedCommentEvent evt, CancellationToken ct)
    {
        if (evt.ActorExternalId == channel.ExternalId)
            return; // ҳамон филтри ҳалқаи CommentAutomationProcessor

        if (await db.FlowSessions.AnyAsync(s => s.Flow.ChannelId == channel.Id && s.TriggerExternalId == evt.CommentId, ct))
            return;

        var flow = await FindMatchingFlowAsync(channel.Id, TriggerTypeComment, evt.Text, evt.MediaId, ct);
        if (flow is null)
            return;

        var contact = await FindOrCreateContactAsync(channel, evt.ActorExternalId, evt.ActorUsername, ct);
        await engine.StartAsync(flow, contact.Id, ct, triggerExternalId: evt.CommentId);
    }

    /// <summary>
    /// DM-ҳо conversationExternalId (= actor-и Instagram)-ро аллакай доранд, чунки
    /// WebhookProcessor.ProcessNewMessagesAsync Conversation-ро пеш аз ин месозад/ёфта мегирад —
    /// пас (бар хилофи коментарий) contact аллакай мавҷуд аст, аз нав сохтан лозим нест.
    /// </summary>
    public async Task ProcessMessageAsync(Channel channel, Conversation contact, ParsedWebhookMessage message, CancellationToken ct)
    {
        if (message.Direction != MessageDirection.Inbound)
            return;

        // Аввал: сессияи intizорӣ (waiting) барои ҳамин contact — новобаста аз кадом flow.
        var waitingSession = await db.FlowSessions
            .FirstOrDefaultAsync(s => s.ContactId == contact.Id && s.Status == FlowSessionStatus.Waiting, ct);
        if (waitingSession is not null)
        {
            if (message.PostbackPayload is not null)
                await engine.ResumeFromButtonAsync(message.PostbackPayload, ct);
            else
                await engine.ResumeFromMessageAsync(waitingSession.Id, message.Body ?? "", ct);
            return;
        }

        if (message.PostbackPayload is not null)
            return; // postback бе сессияи waiting — эҳтимол кӯҳна/бе робита

        if (await db.FlowSessions.AnyAsync(s => s.Flow.ChannelId == channel.Id && s.TriggerExternalId == message.MessageExternalId, ct))
            return;

        var flow = await FindMatchingFlowAsync(channel.Id, TriggerTypeDm, message.Body ?? "", mediaId: null, ct);
        if (flow is null)
            return;

        await engine.StartAsync(flow, contact.Id, ct, triggerExternalId: message.MessageExternalId);
    }

    private async Task<Flow?> FindMatchingFlowAsync(Guid channelId, string triggerType, string text, string? mediaId, CancellationToken ct)
    {
        var flows = await db.Flows
            .Where(f => f.ChannelId == channelId && f.IsActive && f.TriggerType == triggerType)
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(ct);

        foreach (var flow in flows)
        {
            AutomationTriggerConfig triggerConfig;
            try
            {
                triggerConfig = JsonSerializer.Deserialize<AutomationTriggerConfig>(flow.TriggerConfigJson) ?? throw new JsonException("null");
            }
            catch (JsonException ex)
            {
                logger.LogError(ex, "Flow {FlowId}: trigger_config вайрон аст, рад карда шуд", flow.Id);
                continue;
            }

            // Ҳамон CommentAutomationMatcher-и Фазаи 10 — DM-ҳо ҳеҷ гоҳ postScope=selected
            // надоранд (mediaId=null аз ProcessMessageAsync медиҳад, пас он тафтиш худкор true мешавад).
            if (CommentAutomationMatcher.Match(triggerConfig, text, mediaId).Matched)
                return flow;
        }

        return null;
    }

    private async Task<Conversation> FindOrCreateContactAsync(Channel channel, string actorExternalId, string? actorUsername, CancellationToken ct)
    {
        var contact = await db.Conversations.FirstOrDefaultAsync(c => c.ChannelId == channel.Id && c.ExternalId == actorExternalId, ct);
        if (contact is not null)
            return contact;

        contact = new Conversation
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channel.Id,
            ExternalId = actorExternalId,
            ContactUsername = actorUsername,
            Status = ConversationStatus.New,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Conversations.Add(contact);
        await db.SaveChangesAsync(ct);
        return contact;
    }
}
