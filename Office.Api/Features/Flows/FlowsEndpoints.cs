using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.Automation;
using Office.Api.Channels.Flows;
using Office.Api.Channels.Instagram;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Media;

namespace Office.Api.Features.Flows;

public static class FlowsEndpoints
{
    public static IEndpointRouteBuilder MapFlowsEndpoints(this IEndpointRouteBuilder app)
    {
        var byChannel = app.MapGroup("/api/channels/{channelId:guid}/flows").WithTags("Flows");

        byChannel.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Рӯйхати flow-ҳои канал")
            .Produces<IEnumerable<FlowListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byChannel.MapPost("/", CreateAsync)
            .WithValidation<CreateFlowRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Сохтани flow-и холӣ (граф баъдтар аз canvas сабт мешавад)")
            .Produces<FlowDetail>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        byChannel.MapPost("/media", UploadMediaAsync)
            .DisableAntiforgery()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Боркунии медиа (сурат/видео/овоз) барои нодаи паём — attachment_id-и дубора-истифодашаванда")
            .Produces<UploadFlowMediaResult>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        var byFlow = app.MapGroup("/api/flows/{id:guid}").WithTags("Flows");

        byFlow.MapGet("/", GetAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Маълумоти пурраи flow бо граф (nodes+edges)")
            .Produces<FlowDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapPut("/", UpdateAsync)
            .WithValidation<UpdateFlowRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Навсозии ном/триггер")
            .Produces<FlowDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapPut("/graph", UpdateGraphAsync)
            .WithValidation<UpdateFlowGraphRequest>()
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Захираи пурраи граф аз canvas (autosave) — ҳамаи nodes+edges иваз мешаванд")
            .Produces<FlowDetail>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapPatch("/active", SetActiveAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Фаъол/ғайрифаъол кардани flow")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapDelete("/", DeleteAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Нест кардани flow (бо ҳамаи сессияҳояш)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound);

        byFlow.MapGet("/stats", StatsAsync)
            .RequirePermission(Permissions.Channels.Manage)
            .WithSummary("Омори flow: шумораи сессияҳо аз рӯи ҳолат, контактҳо дар ҳар нод")
            .Produces<FlowStats>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> ListAsync(Guid channelId, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            return Results.NotFound();

        var flows = await db.Flows
            .Where(f => f.ChannelId == channelId)
            .OrderByDescending(f => f.CreatedAt)
            .Select(f => new FlowListItem(f.Id, f.ChannelId, f.Name, f.IsActive, f.TriggerType, f.Nodes.Count, f.CreatedAt, f.UpdatedAt))
            .ToListAsync(ct);

        return Results.Ok(flows);
    }

    private static async Task<IResult> CreateAsync(Guid channelId, CreateFlowRequest request, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Channels.AnyAsync(c => c.Id == channelId, ct))
            return Results.NotFound();

        var flow = new Flow
        {
            Id = Guid.CreateVersion7(),
            ChannelId = channelId,
            Name = request.Name,
            IsActive = true,
            TriggerType = request.TriggerType,
            TriggerConfigJson = JsonSerializer.Serialize(request.TriggerConfig),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Flows.Add(flow);
        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/flows/{flow.Id}", ToDetail(flow, [], []));
    }

    private static async Task<IResult> UploadMediaAsync(
        Guid channelId, IFormFile file, AppDbContext db, InstagramProvider instagramProvider,
        IMediaProcessor mediaProcessor, ILogger<Program> logger, CancellationToken ct)
    {
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Id == channelId && c.Type == ChannelType.Instagram, ct);
        if (channel is null)
            return Results.NotFound();

        if (file.Length <= 0)
            return Results.BadRequest();

        var mimeType = string.IsNullOrEmpty(file.ContentType) ? "application/octet-stream" : file.ContentType;
        var (messageType, maxSizeBytes) = MediaUploadValidator.Classify(channel.Type, mimeType);
        if (!MediaUploadValidator.IsWithinLimit(channel.Type, mimeType, file.Length))
        {
            return Results.Problem(
                title: "Файл калон аст",
                detail: $"Барои Instagram ҳадди аксар {maxSizeBytes / (1024 * 1024)} МБ аст.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            string attachmentId;
            string blockType;

            if (messageType == MessageType.Audio)
            {
                // Instagram/Facebook message_attachments audio/webm(opus)-и браузерро (MediaRecorder)
                // рад мекунад — ниг. MediaSendJob.TranscodeVoiceNoteAsync барои далели зиндаи ин
                // (санҷидашуда 2026-08-25). Бояд пеш аз боркунӣ ба aac/m4a иваз шавад. Файли муваққатӣ —
                // Flow media доимӣ нигоҳ дошта намешавад (танҳо attachment_id-и Meta захира мешавад).
                var tempDir = Path.Combine(Path.GetTempPath(), "flow-media-uploads");
                Directory.CreateDirectory(tempDir);
                var sourcePath = Path.Combine(tempDir, $"{Guid.CreateVersion7()}.src");
                var targetPath = Path.ChangeExtension(sourcePath, ".m4a");
                try
                {
                    await using (var sourceStream = File.Create(sourcePath))
                        await file.CopyToAsync(sourceStream, ct);

                    await mediaProcessor.TranscodeToAacAsync(sourcePath, targetPath, ct);

                    await using var transcodedStream = File.OpenRead(targetPath);
                    attachmentId = await instagramProvider.UploadMediaAsync(channel, transcodedStream, "audio/mp4", "voice.m4a", ct);
                    blockType = MessageBlock.TypeAudio;
                }
                finally
                {
                    if (File.Exists(sourcePath)) File.Delete(sourcePath);
                    if (File.Exists(targetPath)) File.Delete(targetPath);
                }
            }
            else
            {
                await using (var stream = file.OpenReadStream())
                    attachmentId = await instagramProvider.UploadMediaAsync(channel, stream, mimeType, file.FileName, ct);

                blockType = messageType switch
                {
                    MessageType.Image => MessageBlock.TypeImage,
                    MessageType.Video => MessageBlock.TypeVideo,
                    _ => MessageBlock.TypeFile,
                };
            }

            string? previewDataUri = messageType is MessageType.Image or MessageType.Video
                ? await TryGenerateThumbnailDataUriAsync(file, mediaProcessor, channelId, logger, ct)
                : null;

            return Results.Ok(new UploadFlowMediaResult(attachmentId, blockType, previewDataUri));
        }
        catch (GraphApiException ex)
        {
            logger.LogError(ex, "Flow media: боркунӣ ба Meta рад шуд (Channel {ChannelId})", channelId);
            return Results.Problem(title: "Боркунӣ ба Instagram рад шуд", detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (MediaProcessingException ex)
        {
            logger.LogError(ex, "Flow media: transcode ноком шуд (Channel {ChannelId})", channelId);
            return Results.Problem(title: "Файл коркард нашуд", detail: "Формати файл дастгирӣ намешавад.", statusCode: StatusCodes.Status400BadRequest);
        }
    }

    /// <summary>32 barobar 128px JPEG хурд (якчанд KB) — дар config_json ҳамчун data URI захира
    /// мешавад (ниг. MessageBlock.PreviewDataUri), пас панел/canvas баъд аз reload низ расмро
    /// нишон дода метавонанд, бе он ки MediaId (attachment_id-и опаку) лозим шавад. Ноком шудани
    /// ин — боркунии асосиро намебандад, танҳо thumbnail намемонад (бе хатои возеҳ ба корбар).</summary>
    private const int ThumbnailMaxDimension = 128;

    private static async Task<string?> TryGenerateThumbnailDataUriAsync(
        IFormFile file, IMediaProcessor mediaProcessor, Guid channelId, ILogger<Program> logger, CancellationToken ct)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "flow-media-thumbs");
        Directory.CreateDirectory(tempDir);
        var sourcePath = Path.Combine(tempDir, $"{Guid.CreateVersion7()}{Path.GetExtension(file.FileName)}");
        var thumbPath = Path.Combine(tempDir, $"{Guid.CreateVersion7()}.jpg");
        try
        {
            await using (var sourceStream = File.Create(sourcePath))
            {
                await using var uploadStream = file.OpenReadStream();
                await uploadStream.CopyToAsync(sourceStream, ct);
            }

            await mediaProcessor.GenerateImageThumbnailAsync(sourcePath, thumbPath, ThumbnailMaxDimension, ct);
            var bytes = await File.ReadAllBytesAsync(thumbPath, ct);
            return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flow media: сохтани thumbnail ноком шуд (Channel {ChannelId})", channelId);
            return null;
        }
        finally
        {
            if (File.Exists(sourcePath)) File.Delete(sourcePath);
            if (File.Exists(thumbPath)) File.Delete(thumbPath);
        }
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        var nodes = await db.FlowNodes.Where(n => n.FlowId == id).ToListAsync(ct);
        var edges = await db.FlowEdges.Where(e => e.FlowId == id).ToListAsync(ct);

        return Results.Ok(ToDetail(flow, nodes, edges));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateFlowRequest request, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        flow.Name = request.Name;
        flow.TriggerType = request.TriggerType;
        flow.TriggerConfigJson = JsonSerializer.Serialize(request.TriggerConfig);
        flow.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var nodes = await db.FlowNodes.Where(n => n.FlowId == id).ToListAsync(ct);
        var edges = await db.FlowEdges.Where(e => e.FlowId == id).ToListAsync(ct);
        return Results.Ok(ToDetail(flow, nodes, edges));
    }

    private static async Task<IResult> UpdateGraphAsync(Guid id, UpdateFlowGraphRequest request, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        foreach (var node in request.Nodes)
        {
            var error = ValidateNodeConfig(node.Type, node.Config);
            if (error is not null)
                return Results.Problem(title: "Config-и нод нодуруст аст", detail: $"Нод {node.Id}: {error}", statusCode: StatusCodes.Status400BadRequest);
        }

        // Иваз кардани пурра — canvas ҳамеша ҳолати комили худро мефиристад (ниг. шарҳи
        // UpdateFlowGraphRequest барои сабаб). Соддатар ва бехатартар аз diff барои autosave.
        var existingNodes = await db.FlowNodes.Where(n => n.FlowId == id).ToListAsync(ct);
        var existingEdges = await db.FlowEdges.Where(e => e.FlowId == id).ToListAsync(ct);
        db.FlowEdges.RemoveRange(existingEdges);
        db.FlowNodes.RemoveRange(existingNodes);

        foreach (var node in request.Nodes)
        {
            db.FlowNodes.Add(new FlowNode
            {
                Id = node.Id,
                FlowId = id,
                Type = Enum.Parse<FlowNodeType>(node.Type, ignoreCase: true),
                ConfigJson = node.Config.GetRawText(),
                X = node.X,
                Y = node.Y,
            });
        }

        foreach (var edge in request.Edges)
        {
            db.FlowEdges.Add(new FlowEdge { Id = edge.Id, FlowId = id, FromNodeId = edge.FromNodeId, FromPort = edge.FromPort, ToNodeId = edge.ToNodeId });
        }

        flow.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.Ok(ToDetail(flow, await db.FlowNodes.Where(n => n.FlowId == id).ToListAsync(ct), await db.FlowEdges.Where(e => e.FlowId == id).ToListAsync(ct)));
    }

    private static async Task<IResult> SetActiveAsync(Guid id, SetFlowActiveRequest request, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        flow.IsActive = request.IsActive;
        flow.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DeleteAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var flow = await db.Flows.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (flow is null)
            return Results.NotFound();

        db.Flows.Remove(flow); // Cascade: FlowNode/FlowEdge/FlowSession(→FlowSessionStep) ҳама бо OnDelete(Cascade)
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> StatsAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        if (!await db.Flows.AnyAsync(f => f.Id == id, ct))
            return Results.NotFound();

        var sessions = await db.FlowSessions.Where(s => s.FlowId == id)
            .Select(s => s.Status)
            .ToListAsync(ct);

        var nodeStats = await db.FlowSessionSteps
            .Where(s => s.Session.FlowId == id)
            .GroupBy(s => s.NodeId)
            .Select(g => new FlowNodeStat(g.Key, g.Select(x => x.SessionId).Distinct().Count()))
            .ToListAsync(ct);

        // 5-тои охирин, на ҳама — Stats popover-и хурд аст, на саҳифаи алоҳида; агар "Ноком" зиёд
        // бошад, флоу-и худ бояд ислоҳ шавад, на рӯйхати дарозро дар popover хонд.
        var recentFailures = await db.FlowSessions
            .Where(s => s.FlowId == id && s.Status == FlowSessionStatus.Failed && s.Error != null)
            .OrderByDescending(s => s.CreatedAt)
            .Take(5)
            .Select(s => new FlowFailure(s.Id, s.Error!, s.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(new FlowStats(
            TotalSessions: sessions.Count,
            FinishedSessions: sessions.Count(s => s == FlowSessionStatus.Finished),
            ActiveOrWaitingSessions: sessions.Count(s => s is FlowSessionStatus.Active or FlowSessionStatus.Waiting),
            FailedSessions: sessions.Count(s => s == FlowSessionStatus.Failed),
            Nodes: nodeStats,
            RecentFailures: recentFailures));
    }

    /// <summary>Ҳар навъи нод config-и typed-и худро дорад — ниг. Channels/Flows/FlowConfigs.cs.</summary>
    private static string? ValidateNodeConfig(string type, JsonElement config)
    {
        try
        {
            switch (type.ToLowerInvariant())
            {
                case "message":
                    var message = config.Deserialize<MessageNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    // "payment" дар намуди JSON қабул карда мешавад (мутобиқат бо спека), вале
                    // қасдан рад мешавад — спека худаш "маҳсулоти пулакӣ"-ро дар НАГИР дорад.
                    if (message.Buttons.Any(b => b.Action == "payment"))
                        return "Тугмаи навъи 'payment' дастгирӣ намешавад.";
                    if (message.Buttons.Any(b => b.Action != MessageButton.ActionNext && b.Action != MessageButton.ActionUrl))
                        return "action-и тугма бояд 'next' ё 'url' бошад.";
                    break;
                case "condition":
                    _ = config.Deserialize<ConditionNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    break;
                case "action":
                    var action = config.Deserialize<ActionNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    if (string.IsNullOrEmpty(action.Kind))
                        return "kind лозим аст.";
                    break;
                case "note":
                    _ = config.Deserialize<NoteNodeConfig>(FlowJsonOptions.Options) ?? throw new JsonException("null");
                    break;
                default:
                    return $"Навъи нодуруст: {type}.";
            }
            return null;
        }
        catch (JsonException ex)
        {
            return $"config хонда нашуд: {ex.Message}";
        }
    }

    private static FlowDetail ToDetail(Flow flow, List<FlowNode> nodes, List<FlowEdge> edges) => new(
        flow.Id,
        flow.ChannelId,
        flow.Name,
        flow.IsActive,
        flow.TriggerType,
        JsonSerializer.Deserialize<AutomationTriggerConfig>(flow.TriggerConfigJson)!,
        nodes.Select(n => new FlowNodeDto(n.Id, n.Type.ToString().ToLowerInvariant(), JsonDocument.Parse(n.ConfigJson).RootElement, n.X, n.Y)).ToList(),
        edges.Select(e => new FlowEdgeDto(e.Id, e.FromNodeId, e.FromPort, e.ToNodeId)).ToList(),
        flow.CreatedAt,
        flow.UpdatedAt);
}
