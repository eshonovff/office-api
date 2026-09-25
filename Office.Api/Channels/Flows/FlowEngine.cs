using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Instagram;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Натиҷаи иҷрои як нод: Port (баромади интихобшуда, агар маълум бошад ҳозир), WaitReason
/// (агар сессия бояд waiting монад — Port дар ин ҳолат ҳанӯз номаълум аст, баъдтар аз рӯи
/// рӯйдоди резюме муайян мешавад), EndSession (goto_flow — ин сессия бе идомаи худаш тамом мешавад).
/// </summary>
internal record NodeOutcome(string? Port, FlowWaitReason? WaitReason, bool EndSession = false);

/// <summary>
/// Ҳаракатчии иҷрои Flow (Фазаи 12) — версияи session-based-и модели "як қоида" (ниг.
/// CommentAutomationProcessor/Job-и Фазаи 10/11). Ҳар қадам як FlowSessionStep месозад (барои
/// омор), ва FlowSession.Status/CurrentNodeId/StepCount-ро навсозӣ мекунад. Хатогиҳои Graph API
/// дар дохили executor-ҳо catch НАМЕШАВАНД қасдан барои message/condition (агар Meta хато диҳад,
/// беҳтар аст сессия Failed шавад бо сабаби возеҳ, то хомӯшона идома ёбад бо маълумоти нодуруст) —
/// фарқ аз CommentAutomationJob, ки паём/DM мустақил буданд; дар граф, як қадами ноком метавонад
/// маънои идомаи хатарнок дошта бошад.
/// </summary>
public class FlowEngine(AppDbContext db, InstagramProvider instagramProvider, IBackgroundJobClient backgroundJobs, HttpClient httpClient, ILogger<FlowEngine> logger)
{
    /// <summary>
    /// triggerExternalId (comment_id/message_id) — танҳо захира мешавад, ИДЕМПОТЕНТӢ дар ин ҷо
    /// САНҶИДА НАМЕШАВАД (масъулияти FlowTriggerProcessor, ки пеш аз даъвати StartAsync тафтиш
    /// мекунад) — чунки goto_flow низ StartAsync-ро даъват мекунад, бе ягон рӯйдоди берунӣ.
    ///
    /// gotoChain: занҷири flow-ҳои аллакай тайшуда дар ҳамин силсилаи ҳамзамони goto_flow (на
    /// байни сессияҳои алоҳида). Бе ин, ду flow ки ба ҳам goto_flow доранд (A→B→A→...)
    /// StackOverflowException месозанд (реcursия бе марз, ки процесси .NET-ро мекушад — на
    /// хатои қобили catch). null=аввалин даъват (аз FlowTriggerProcessor), goto_flow худаш
    /// занҷири ҷориро мегузаронад.
    /// </summary>
    public async Task StartAsync(
        Flow flow, Guid contactId, CancellationToken ct, string? triggerExternalId = null, HashSet<Guid>? gotoChain = null)
    {
        gotoChain ??= [];
        if (!gotoChain.Add(flow.Id))
            throw new InvalidOperationException($"goto_flow: ҳалқаи бемарҳила ёфт шуд — flow {flow.Id} аллакай дар ҳамин занҷир аст.");

        var (nodes, edges) = await LoadGraphAsync(flow.Id, ct);
        var nodesById = nodes.ToDictionary(n => n.Id);

        // "Ноди аввал" = нод бе ягон edge-и воридотӣ.
        var startNode = nodes.FirstOrDefault(n => edges.All(e => e.ToNodeId != n.Id));
        if (startNode is null)
        {
            logger.LogWarning("Flow {FlowId}: граф холист ё ноди аввал ёфт нашуд, сессия сохта нашуд", flow.Id);
            return;
        }

        var session = new FlowSession
        {
            Id = Guid.CreateVersion7(),
            FlowId = flow.Id,
            ContactId = contactId,
            TriggerExternalId = triggerExternalId,
            VariablesJson = "{}",
            Status = FlowSessionStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.FlowSessions.Add(session);

        await RunLoopAsync(session, nodesById, edges, startNode.Id, ct, gotoChain);
    }

    public async Task ResumeFromDelayAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await LoadSessionAsync(sessionId, ct);
        if (session is null || session.Status != FlowSessionStatus.Waiting || session.WaitReason != FlowWaitReason.Delay || session.CurrentNodeId is null)
            return;

        var (nodes, edges) = await LoadGraphAsync(session.FlowId, ct);
        session.ScheduledJobId = null;
        await AdvanceViaPortAsync(session, nodes.ToDictionary(n => n.Id), edges, session.CurrentNodeId.Value, "default", ct);
    }

    /// <summary>
    /// payload-и postback шакли "{sessionId}:{nodeId}:{buttonIndex}" дорад — ниг.
    /// ExecuteMessageNodeAsync (ҳамон ҷо сохта мешавад). AllowRepeat=true агар сессия аллакай
    /// аз ин нод гузашта бошад ҳам, боз иҷозат медиҳад (масалан тугмаи "Менюи асосӣ" дар ҳар
    /// паём) — ниг. ҳуҷҷати фазаи 12 барои тафсири ин интихоб (спека операторҳои дақиқро намедод).
    /// </summary>
    public async Task ResumeFromButtonAsync(string postbackPayload, CancellationToken ct)
    {
        var parts = postbackPayload.Split(':');
        if (parts.Length != 3 || !Guid.TryParse(parts[0], out var sessionId) || !Guid.TryParse(parts[1], out var nodeId) ||
            !int.TryParse(parts[2], out var buttonIndex))
        {
            return; // на аз ин flow (ё вайрон) — эҳтимол postback-и дигар система
        }

        var session = await LoadSessionAsync(sessionId, ct);
        if (session is null)
            return;

        var (nodes, edges) = await LoadGraphAsync(session.FlowId, ct);
        var nodesById = nodes.ToDictionary(n => n.Id);
        if (!nodesById.TryGetValue(nodeId, out var node) || node.Type != FlowNodeType.Message)
            return;

        var config = Deserialize<MessageNodeConfig>(node.ConfigJson);
        if (buttonIndex < 0 || buttonIndex >= config.Buttons.Length)
            return;

        var isCurrentWait = session.Status == FlowSessionStatus.Waiting && session.WaitReason == FlowWaitReason.ButtonClick && session.CurrentNodeId == nodeId;
        if (!isCurrentWait && !config.Buttons[buttonIndex].AllowRepeat)
            return;

        session.Status = FlowSessionStatus.Active;
        await AdvanceViaPortAsync(session, nodesById, edges, nodeId, $"button:{buttonIndex}", ct);
    }

    public async Task ResumeFromMessageAsync(Guid sessionId, string messageBody, CancellationToken ct)
    {
        var session = await LoadSessionAsync(sessionId, ct);
        if (session is null || session.Status != FlowSessionStatus.Waiting || session.CurrentNodeId is not { } nodeId)
            return;

        var (nodes, edges) = await LoadGraphAsync(session.FlowId, ct);
        var nodesById = nodes.ToDictionary(n => n.Id);

        if (session.WaitReason == FlowWaitReason.WindowClosed)
        {
            // Тирезаи 24-соата эҳтимол акнун кушода шуд (паёми нав омад) — ҳамон нод аз нав
            // кӯшиш мекунад (набояд бо порт идома ёбад, чунки паёми он ҳанӯз ҳеҷ гоҳ нарафтааст).
            await RunLoopAsync(session, nodesById, edges, nodeId, ct);
            return;
        }

        if (session.WaitReason == FlowWaitReason.CollectInput && nodesById.TryGetValue(nodeId, out var node))
        {
            var config = Deserialize<ActionNodeConfig>(node.ConfigJson);
            if (config.VariableKey is not null)
                await SetVariableAsync(session, config.VariableKey, messageBody, ct);

            session.Status = FlowSessionStatus.Active;
            await AdvanceViaPortAsync(session, nodesById, edges, nodeId, "default", ct);
        }

        // WaitReason=Delay/ButtonClick: паёми оддии корбар ба ин сессия дахл надорад — партофта мешавад.
    }

    private async Task AdvanceViaPortAsync(
        FlowSession session, Dictionary<Guid, FlowNode> nodesById, List<FlowEdge> edges, Guid fromNodeId, string port, CancellationToken ct)
    {
        var edge = edges.FirstOrDefault(e => e.FromNodeId == fromNodeId && e.FromPort == port);
        if (edge is null)
        {
            session.Status = FlowSessionStatus.Finished;
            session.WaitReason = null;
            await db.SaveChangesAsync(ct);
            return;
        }

        session.WaitReason = null;
        await RunLoopAsync(session, nodesById, edges, edge.ToNodeId, ct);
    }

    private async Task RunLoopAsync(
        FlowSession session, Dictionary<Guid, FlowNode> nodesById, List<FlowEdge> edges, Guid currentNodeId, CancellationToken ct,
        HashSet<Guid>? gotoChain = null)
    {
        // Резюме (на StartAsync-и тоза) ин параметрро намедиҳад — бо flow-и ХУДИ ҳамин сессия
        // сар мешавад, то агар граф баъдтар ба goto_flow бирасад ва ба ҳамин flow БАРГАРДАД,
        // ин ҳам ҳамчун ҳалқа ҳисоб шавад (на танҳо ҳалқаҳои дар лаҳзаи StartAsync оғозёфта).
        gotoChain ??= [session.FlowId];

        // Every run — a fresh start, a delay firing, a button, a reply — passes here, so this one
        // check also stops sessions that were waiting when the мизоҷ's plan ran out.
        var channelId = await db.Flows.Where(f => f.Id == session.FlowId).Select(f => f.ChannelId).FirstAsync(ct);
        if (!await AutomationRunGate.CanRunAsync(channelId, db, ct))
        {
            session.Status = FlowSessionStatus.Failed;
            session.Error = "Автоматизатсия қатъ шуд: тарифи соҳиби канал фаъол нест ё канал ҷудо шудааст.";
            await db.SaveChangesAsync(ct);
            return;
        }

        while (true)
        {
            if (FlowSessionLoopGuard.ShouldStop(session.StepCount))
            {
                session.Status = FlowSessionStatus.Failed;
                session.Error = $"Ҳимояи ҳалқа: аз {FlowSessionLoopGuard.MaxSteps} қадам гузашт.";
                break;
            }

            if (!nodesById.TryGetValue(currentNodeId, out var node))
            {
                session.Status = FlowSessionStatus.Failed;
                session.Error = $"Нод {currentNodeId} дар граф ёфт нашуд.";
                break;
            }

            session.CurrentNodeId = currentNodeId;

            NodeOutcome outcome;
            try
            {
                outcome = await ExecuteNodeAsync(session, node, ct, gotoChain);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "FlowSession {SessionId}: нод {NodeId} ноком шуд", session.Id, currentNodeId);
                session.Status = FlowSessionStatus.Failed;
                session.Error = ex.Message;
                break;
            }

            session.StepCount++;
            db.FlowSessionSteps.Add(new FlowSessionStep
            {
                Id = Guid.CreateVersion7(),
                SessionId = session.Id,
                NodeId = currentNodeId,
                FromPort = outcome.Port,
                CreatedAt = DateTimeOffset.UtcNow,
            });

            if (outcome.EndSession)
            {
                session.Status = FlowSessionStatus.Finished;
                break;
            }

            if (outcome.WaitReason is not null)
            {
                session.Status = FlowSessionStatus.Waiting;
                session.WaitReason = outcome.WaitReason;
                break;
            }

            if (outcome.Port is null)
            {
                session.Status = FlowSessionStatus.Finished;
                break;
            }

            var edge = edges.FirstOrDefault(e => e.FromNodeId == currentNodeId && e.FromPort == outcome.Port);
            if (edge is null)
            {
                session.Status = FlowSessionStatus.Finished;
                break;
            }

            currentNodeId = edge.ToNodeId;
        }

        await db.SaveChangesAsync(ct);
    }

    private Task<NodeOutcome> ExecuteNodeAsync(FlowSession session, FlowNode node, CancellationToken ct, HashSet<Guid> gotoChain) => node.Type switch
    {
        FlowNodeType.Message => ExecuteMessageNodeAsync(session, node, ct),
        FlowNodeType.Condition => ExecuteConditionNodeAsync(session, node, ct),
        FlowNodeType.Action => ExecuteActionNodeAsync(session, node, ct, gotoChain),
        _ => Task.FromResult(new NodeOutcome("default", null)), // Note: аннотатсияи canvas-и холис, ба иҷро дахл надорад.
    };

    private async Task<NodeOutcome> ExecuteMessageNodeAsync(FlowSession session, FlowNode node, CancellationToken ct)
    {
        var contact = await db.Conversations.Include(c => c.Channel).FirstAsync(c => c.Id == session.ContactId, ct);

        var config = Deserialize<MessageNodeConfig>(node.ConfigJson);
        var variables = await LoadVariablesAsync(session, ct);
        var contactFields = BuildContactFields(contact);

        var textBlocks = config.Blocks.Where(b => b.Type == MessageBlock.TypeText && !string.IsNullOrEmpty(b.Text));
        var text = string.Join("\n\n", textBlocks.Select(b => FlowVariableInterpolator.Interpolate(b.Text!, variables, contactFields)));

        // Якто блоки media дар як нод (расм/видео/овоз/файл) — MediaId аллакай attachment_id-и
        // дубора-истифодашавандаи Meta (боркунии воқеӣ дар лаҳзаи илова кардан дар canvas рафта
        // буд, ниг. FlowsEndpoints.UploadMediaAsync), пас ин ҷо танҳо фиристодан лозим аст, на
        // боркунӣ. Якчанд media дар як нод дастгирӣ намешавад — барои дуюм, нодаи дигари паём илова кунед.
        var mediaBlock = config.Blocks.FirstOrDefault(b => b.Type != MessageBlock.TypeText && !string.IsNullOrEmpty(b.MediaId));
        var mediaType = mediaBlock is null ? (MessageType?)null : ToMessageType(mediaBlock.Type);

        // Контакте, ки ҳеҷ гоҳ ба мо DM нафиристодааст (масалан танҳо коментарий кардааст):
        // WindowExpiresAt=null маънои "тирезаи 24-соата ҳеҷ гоҳ КУШОДА НАШУДААСТ" дорад, на
        // "баста" — IsWindowClosed поён барои null ҳамеша false бармегардонад, пас бе ин шоха
        // ба SendMessageAsync-и муқаррарӣ мерафтем, ки Meta онро рад мекунад (ҳеҷ тиреза нест,
        // ҳеҷ гоҳ кушода нашудааст — санҷидашуда: 2026-09-17, дар воқеъ ин сабаби "ба DM ҳеҷ чиз
        // намеояд" барои коментатори аввалин буд, вақте ки нод тугмаи "next" дошт). Ягона роҳи
        // қонунии расидан ба чунин контакт — Private Reply бо comment_id (як бор, дар давоми 7 рӯз).
        // ≤1 тугма (ҳар навъ — url ё postback) дар ҳамин як фиристодан ҷобаҷо мешавад; 2-3 тугма
        // ҳанӯз дастгирӣ намешавад (интихоби кадоме бе аз даст додани дигарон мушаххас нест) —
        // барои он ҳолат ба рафтори кӯҳна (тирезаи муқаррарӣ, эҳтимол рад) мегузарем.
        if (session.TriggerExternalId is not null && contact.WindowExpiresAt is null && config.Buttons.Length <= 1)
        {
            // Private Reply "як бор дар як коментарий" аст — на ҳама чиз якҷоя фиристода мешавад.
            // Тугма (агар бошад) авлотар аст аз media: бе тугма, flow-и graph-based (масалан
            // "wants_guide"-и Лид-магнит) идома ёфта наметавонад — нарасидани расм танҳо масъалаи
            // зоҳирист. Матн ҳамеша бо тугма меравад (ҳамон паём); бо media ноком мешавад.
            if (config.Buttons.Length == 1)
            {
                var configButton = config.Buttons[0];
                var isUrl = configButton.Action == MessageButton.ActionUrl;
                var button = new InstagramSendButton(
                    configButton.Title, isUrl ? InstagramSendButton.TypeWebUrl : InstagramSendButton.TypePostback,
                    configButton.Url, isUrl ? null : $"{session.Id}:{node.Id}:0");

                if (mediaBlock is not null)
                    logger.LogWarning("FlowSession {SessionId}: media дар нод {NodeId} бо тугма якҷоя буд — Private Reply танҳо якто ирсол мекунад, тугма авлотар шуд", session.Id, node.Id);

                await instagramProvider.SendPrivateReplyAsync(contact.Channel, session.TriggerExternalId, text, button, ct);
                // Тугмаи "url" рӯйдоди postback намедиҳад (танҳо силка мекушояд) — интизории он
                // ҷаворобе абадӣ мемонд; рафтори кӯҳна (идомаи фаврӣ) барои он нигоҳ дошта шуд.
                return isUrl ? new NodeOutcome("default", null) : new NodeOutcome(null, FlowWaitReason.ButtonClick);
            }

            if (mediaBlock is not null && mediaType is not null)
            {
                if (!string.IsNullOrEmpty(text))
                    logger.LogWarning("FlowSession {SessionId}: матни нод {NodeId} бо media якҷоя буд — Private Reply танҳо якто ирсол мекунад, media авлотар шуд", session.Id, node.Id);
                await instagramProvider.SendPrivateReplyMediaAsync(contact.Channel, session.TriggerExternalId, mediaBlock.MediaId!, mediaType.Value, ct);
            }
            else if (!string.IsNullOrEmpty(text))
            {
                await instagramProvider.SendPrivateReplyAsync(contact.Channel, session.TriggerExternalId, text, null, ct);
            }
            return new NodeOutcome("default", null);
        }

        // Спека: "Пеш аз ҳар фиристодан дар runtime тирезаи 24-соата тафтиш шавад; агар баста
        // бошад — сессия waiting монад, на failed". isTemplate=false — Instagram шаблон надорад
        // (SendTemplateAsync-и InstagramProvider NotSupportedException медиҳад), пас ҳеҷ роҳи
        // дигари убур аз тиреза нест — бояд интизор шуд.
        if (ConversationWindowCalculator.IsWindowClosed(isTemplate: false, contact.WindowExpiresAt, DateTimeOffset.UtcNow))
            return new NodeOutcome(null, FlowWaitReason.WindowClosed);

        if (config.Buttons.Length > 0)
        {
            // Send API-и Meta як message object мегирад (attachment/button-template якҷоя
            // намешаванд) — агар media ҳам бошад, аввал ҳамчун паёми алоҳида (бе caption, то
            // матн дучандшуда набошад) мефиристем, баъд паёми тугмадор.
            if (mediaBlock is not null && mediaType is not null)
            {
                await instagramProvider.SendMediaMessageAsync(
                    contact.Channel, contact.ExternalId, mediaBlock.MediaId!, mediaType.Value,
                    caption: null, isVoiceNote: mediaType == MessageType.Audio, messageTag: null, ct);
            }

            var buttons = config.Buttons.Select((button, index) => button.Action == MessageButton.ActionUrl
                ? new InstagramSendButton(button.Title, InstagramSendButton.TypeWebUrl, button.Url, null)
                : new InstagramSendButton(button.Title, InstagramSendButton.TypePostback, null, $"{session.Id}:{node.Id}:{index}"))
                .ToArray();

            await instagramProvider.SendButtonMessageAsync(contact.Channel, contact.ExternalId, text, buttons, null, ct);
            return new NodeOutcome(null, FlowWaitReason.ButtonClick);
        }

        if (mediaBlock is not null && mediaType is not null)
        {
            // caption: SendMediaMessageAsync худаш баъд аз attachment як паёми матнии ҷудогона
            // мефиристад (Send API як object мегирад) — тирезаи муқаррарӣ маҳдудияти "як бор"-и
            // Private Reply-ро надорад, пас ин ҷо ҳарду (media+матн) бехатар кор мекунанд.
            await instagramProvider.SendMediaMessageAsync(
                contact.Channel, contact.ExternalId, mediaBlock.MediaId!, mediaType.Value,
                caption: text, isVoiceNote: mediaType == MessageType.Audio, messageTag: null, ct);
        }
        else if (!string.IsNullOrEmpty(text))
        {
            await instagramProvider.SendMessageAsync(contact.Channel, contact.ExternalId, text, null, ct);
        }

        return new NodeOutcome("default", null);
    }

    private static MessageType ToMessageType(string blockType) => blockType switch
    {
        MessageBlock.TypeImage => MessageType.Image,
        MessageBlock.TypeVideo => MessageType.Video,
        MessageBlock.TypeAudio => MessageType.Audio,
        _ => MessageType.File,
    };

    private async Task<NodeOutcome> ExecuteConditionNodeAsync(FlowSession session, FlowNode node, CancellationToken ct)
    {
        var config = Deserialize<ConditionNodeConfig>(node.ConfigJson);

        bool? isFollowing = null;
        if (config.Rules.Any(r => r.Field == ConditionRule.FieldSubscription))
        {
            var contact = await db.Conversations.Include(c => c.Channel).FirstAsync(c => c.Id == session.ContactId, ct);
            // Ҳамон CheckFollowStatusAsync-и Фазаи 11 — кэш/буҷаи соатии он бетағйир истифода мешавад.
            isFollowing = await instagramProvider.CheckFollowStatusAsync(contact.Channel, contact.ExternalId, ct) == FollowCheckResult.Following;
        }

        var tags = config.Rules.Any(r => r.Field == ConditionRule.FieldTags)
            ? (await db.ContactTags.Where(t => t.ContactId == session.ContactId).Select(t => t.Tag).ToListAsync(ct)).ToHashSet()
            : [];

        var variables = await LoadVariablesAsync(session, ct);
        // Вақти МАҲАЛЛӢ (Душанбе, +5), на UTC-и хом — вагарна шарти "аз соати 9 то 18" воқеан
        // аз 14:00 то 23:00 UTC санҷида мешуд (5 соат нодуруст), бе ягон хатои возеҳ дар лог.
        var context = new ConditionContext(variables, tags, isFollowing, OfficeLocalDate.Now(DateTimeOffset.UtcNow));
        var matched = ConditionEvaluator.Evaluate(config, context);

        return new NodeOutcome(matched ? "match" : "nomatch", null);
    }

    private async Task<NodeOutcome> ExecuteActionNodeAsync(FlowSession session, FlowNode node, CancellationToken ct, HashSet<Guid> gotoChain)
    {
        var config = Deserialize<ActionNodeConfig>(node.ConfigJson);

        switch (config.Kind)
        {
            case ActionNodeConfig.KindDelay:
            {
                var delay = TimeSpan.FromMinutes(Math.Max(config.DelayMinutes ?? 0, 0));
                session.ScheduledJobId = backgroundJobs.Schedule<FlowEngineJob>(j => j.ResumeFromDelayAsync(session.Id, CancellationToken.None), delay);
                return new NodeOutcome(null, FlowWaitReason.Delay);
            }

            case ActionNodeConfig.KindAddTags:
                await AddTagsAsync(session.ContactId, config.Tags ?? [], ct);
                return new NodeOutcome("default", null);

            case ActionNodeConfig.KindRemoveTags:
                await RemoveTagsAsync(session.ContactId, config.Tags ?? [], ct);
                return new NodeOutcome("default", null);

            case ActionNodeConfig.KindSetVariable when config.VariableKey is not null:
            {
                var contact = await db.Conversations.FirstAsync(c => c.Id == session.ContactId, ct);
                var variables = await LoadVariablesAsync(session, ct);
                var value = FlowVariableInterpolator.Interpolate(config.VariableValue ?? "", variables, BuildContactFields(contact));
                await SetVariableAsync(session, config.VariableKey, value, ct);
                return new NodeOutcome("default", null);
            }

            case ActionNodeConfig.KindCollectInput:
                return new NodeOutcome(null, FlowWaitReason.CollectInput);

            case ActionNodeConfig.KindHttpRequest when config.HttpUrl is not null:
                await ExecuteHttpRequestAsync(session, config, ct);
                return new NodeOutcome("default", null); // хатои хидмати берунӣ flow-ро намебандад — қасдан идома меёбад

            case ActionNodeConfig.KindGotoFlow when config.TargetFlowId is not null:
            {
                // Same channel only. The contact belongs to this flow's channel, and the engine runs
                // in a background job with no tenant filter — a bare id lookup let one owner's flow
                // start another owner's (another мизоҷ's or the company's) flow for this contact.
                var currentChannelId = await db.Flows
                    .Where(f => f.Id == session.FlowId).Select(f => f.ChannelId).FirstAsync(ct);
                var targetFlow = await db.Flows.FirstOrDefaultAsync(
                    f => f.Id == config.TargetFlowId && f.IsActive && f.ChannelId == currentChannelId, ct);
                if (targetFlow is not null)
                    await StartAsync(targetFlow, session.ContactId, ct, gotoChain: gotoChain);
                return new NodeOutcome(null, null, EndSession: true);
            }

            default:
                return new NodeOutcome("default", null);
        }
    }

    private async Task ExecuteHttpRequestAsync(FlowSession session, ActionNodeConfig config, CancellationToken ct)
    {
        if (!HttpRequestUrlGuard.IsAllowed(config.HttpUrl!))
        {
            logger.LogWarning("FlowSession {SessionId}: http_request URL рад шуд (маҳдудияти SSRF): {Url}", session.Id, config.HttpUrl);
            return;
        }

        try
        {
            var contact = await db.Conversations.FirstAsync(c => c.Id == session.ContactId, ct);
            var variables = await LoadVariablesAsync(session, ct);
            var body = config.HttpBodyTemplate is null
                ? null
                : FlowVariableInterpolator.Interpolate(config.HttpBodyTemplate, variables, BuildContactFields(contact));

            using var request = new HttpRequestMessage(new HttpMethod(config.HttpMethod ?? "POST"), config.HttpUrl!);
            if (body is not null)
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");

            var response = await httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "FlowSession {SessionId}: http_request ба {Url} {StatusCode} баргардонд", session.Id, config.HttpUrl, (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "FlowSession {SessionId}: http_request ба {Url} ноком шуд", session.Id, config.HttpUrl);
        }
    }

    private async Task AddTagsAsync(Guid contactId, string[] tags, CancellationToken ct)
    {
        if (tags.Length == 0)
            return;

        // Ду манбаъ якҷоя мешаванд: сатрҳои аллакай захирашуда ДАР DB, ва сатрҳое, ки дар ҳамин
        // давр (масалан goto_flow ё ҳалқа) аллакай Add шудаанд, вале ҳанӯз SaveChangesAsync
        // нашудаанд — RunLoopAsync танҳо ДАР ОХИР сабт мекунад. Бе тафтиши дуюм, тегҳои
        // такрории дар ҳамин давр (масалан ду нод, ки ҳарду ҳамон тегро илова мекунанд) EF-ро бо
        // "already tracked with the same key" вайрон мекард (санҷидашуда: LoopGuard-и тести дар боло).
        var existingInDb = await db.ContactTags.Where(t => t.ContactId == contactId).Select(t => t.Tag).ToListAsync(ct);
        var existingTracked = db.ChangeTracker.Entries<ContactTag>()
            .Where(e => e.State != EntityState.Deleted && e.Entity.ContactId == contactId)
            .Select(e => e.Entity.Tag);
        var existing = existingInDb.Concat(existingTracked).ToHashSet();

        foreach (var tag in tags)
        {
            if (existing.Add(tag))
                db.ContactTags.Add(new ContactTag { ContactId = contactId, Tag = tag, CreatedAt = DateTimeOffset.UtcNow });
        }
    }

    private async Task RemoveTagsAsync(Guid contactId, string[] tags, CancellationToken ct)
    {
        if (tags.Length == 0)
            return;

        var rows = await db.ContactTags.Where(t => t.ContactId == contactId && tags.Contains(t.Tag)).ToListAsync(ct);
        db.ContactTags.RemoveRange(rows);
    }

    /// <summary>
    /// Ҳарду сарчашма якҷоя мешаванд: ContactVariable (доимӣ, байни ҳама flow-ҳо мубодила
    /// мешавад) ва FlowSession.VariablesJson (танҳо доираи ин сессия — масалан ҳисоби муваққатӣ).
    /// Сессия болотар меравад агар калиди якхела бошад.
    /// </summary>
    private async Task<Dictionary<string, string>> LoadVariablesAsync(FlowSession session, CancellationToken ct)
    {
        var result = await db.ContactVariables.Where(v => v.ContactId == session.ContactId).ToDictionaryAsync(v => v.Key, v => v.Value, ct);

        var sessionVars = JsonSerializer.Deserialize<Dictionary<string, string>>(session.VariablesJson) ?? [];
        foreach (var (key, value) in sessionVars)
            result[key] = value;

        return result;
    }

    private async Task SetVariableAsync(FlowSession session, string key, string value, CancellationToken ct)
    {
        var sessionVars = JsonSerializer.Deserialize<Dictionary<string, string>>(session.VariablesJson) ?? [];
        sessionVars[key] = value;
        session.VariablesJson = JsonSerializer.Serialize(sessionVars);

        var existing = await db.ContactVariables.FirstOrDefaultAsync(v => v.ContactId == session.ContactId && v.Key == key, ct);
        if (existing is null)
            db.ContactVariables.Add(new ContactVariable { ContactId = session.ContactId, Key = key, Value = value });
        else
            existing.Value = value;
    }

    /// <summary>
    /// Майдонҳои built-in барои {{key}} — ниг. спека: "clientId, firstName, lastName, fullName,
    /// username, chatLink". Conversation ном/насабро ҷудо нигоҳ намедорад — тахминан ҷудо мекунем
    /// (аввалин калима = firstName), чунки Meta ин ду майдонро алоҳида намедиҳад.
    /// </summary>
    private static Dictionary<string, string> BuildContactFields(Conversation contact)
    {
        var fullName = contact.ContactName ?? contact.ContactUsername ?? "";
        var spaceIndex = fullName.IndexOf(' ');

        return new Dictionary<string, string>
        {
            ["clientId"] = contact.ExternalId,
            ["fullName"] = fullName,
            ["firstName"] = spaceIndex > 0 ? fullName[..spaceIndex] : fullName,
            ["lastName"] = spaceIndex > 0 ? fullName[(spaceIndex + 1)..] : "",
            ["username"] = contact.ContactUsername ?? "",
            ["chatLink"] = $"/inbox?conversation={contact.Id}",
        };
    }

    private Task<FlowSession?> LoadSessionAsync(Guid sessionId, CancellationToken ct) =>
        db.FlowSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);

    private async Task<(List<FlowNode> Nodes, List<FlowEdge> Edges)> LoadGraphAsync(Guid flowId, CancellationToken ct)
    {
        var nodes = await db.FlowNodes.Where(n => n.FlowId == flowId).ToListAsync(ct);
        var edges = await db.FlowEdges.Where(e => e.FlowId == flowId).ToListAsync(ct);
        return (nodes, edges);
    }

    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, FlowJsonOptions.Options) ?? throw new JsonException($"null {typeof(T).Name}");
}
