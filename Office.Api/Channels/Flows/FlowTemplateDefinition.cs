using System.Text.Json;
using Microsoft.Extensions.Logging;
using Office.Api.Channels.Instagram;
using Office.Api.Data.Entities;
using Office.Api.Media;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Шакли typed-и FlowTemplate.DefinitionJson. Key (на Guid) — рамзи муваққатии дохили худи
/// шаблон, танҳо барои пайванди Edge→Node дар ҳамин JSON; ҳангоми нусхабардорӣ ба Flow-и воқеӣ
/// (POST .../flows/from-template), Key→Guid-и воқеӣ табдил меёбад.
///
/// DefaultImageAsset (ихтиёрӣ): номи файл дар Assets/DefaultTemplateImages/ — агар дода шавад,
/// FlowTemplatesEndpoints.InstantiateAsync ин суратро ба КАНАЛИ мушаххас бор мекунад (attachment_id
/// умумӣ буда наметавонад — ҳар акаунти Instagram attachment_id-и худро мехоҳад) ва ҳамчун блоки
/// расм ба config-и ин нод замима мекунад, пеш аз захира.
/// </summary>
public record FlowTemplateNodeDefinition(string Key, string Type, JsonElement Config, double X, double Y, string? DefaultImageAsset = null);

public record FlowTemplateEdgeDefinition(string FromKey, string FromPort, string ToKey);

public record FlowTemplateDefinition(FlowTemplateNodeDefinition[] Nodes, FlowTemplateEdgeDefinition[] Edges);

/// <summary>
/// Key→Guid-и воқеӣ (боло) — як ҷо, то FlowTemplatesEndpoints ва тестҳо (FlowTemplateDeliveryTests)
/// айнан ҳамон мантиқро истифода баранд, на ду нусхаи мустақил, ки метавонанд аз ҳам дур шаванд.
/// </summary>
public static class FlowTemplateInstantiator
{
    public static (List<FlowNode> Nodes, List<FlowEdge> Edges) Instantiate(FlowTemplateDefinition definition, Guid flowId)
    {
        var idByKey = definition.Nodes.ToDictionary(n => n.Key, _ => Guid.CreateVersion7());

        var nodes = definition.Nodes.Select(node => new FlowNode
        {
            Id = idByKey[node.Key],
            FlowId = flowId,
            Type = Enum.Parse<FlowNodeType>(node.Type, ignoreCase: true),
            ConfigJson = node.Config.GetRawText(),
            X = node.X,
            Y = node.Y,
        }).ToList();

        var edges = definition.Edges.Select(edge => new FlowEdge
        {
            Id = Guid.CreateVersion7(),
            FlowId = flowId,
            FromNodeId = idByKey[edge.FromKey],
            FromPort = edge.FromPort,
            ToNodeId = idByKey[edge.ToKey],
        }).ToList();

        return (nodes, edges);
    }

    /// <summary>
    /// Расми пешфарзи баъзе нодҳо (масалан паёми оғозини "Лид-магнит") — attachment_id-и Meta
    /// умумӣ буда наметавонад (ҳар акаунти Instagram аз худ мехоҳад), пас файли Assets/-ро
    /// ҳамин ҷо, барои КАНАЛИ мушаххас, як бор бор мекунем. Хатогӣ (канали ҳанӯз пайваст
    /// нашуда, Meta дастрас нест ва ғ.) сохтани flow-ро намебандад — паём бе расм фиристода
    /// мешавад, на хатои 500. `nodes` бояд натиҷаи ҳамин Instantiate барои ҳамин `definition`
    /// бошад — тартиб бояд мувофиқат кунад (Zip бо definition.Nodes).
    /// </summary>
    public static async Task AttachDefaultImagesAsync(
        FlowTemplateDefinition definition, List<FlowNode> nodes, Channel channel,
        InstagramProvider instagramProvider, IMediaProcessor mediaProcessor, ILogger logger, CancellationToken ct)
    {
        if (channel.Type != ChannelType.Instagram || string.IsNullOrEmpty(channel.CredentialsEncrypted))
            return;

        var nodeByKey = definition.Nodes.Zip(nodes).ToDictionary(pair => pair.First.Key, pair => pair.Second);
        foreach (var templateNode in definition.Nodes)
        {
            if (templateNode.DefaultImageAsset is null || !nodeByKey.TryGetValue(templateNode.Key, out var node))
                continue;

            try
            {
                var assetPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DefaultTemplateImages", templateNode.DefaultImageAsset);
                if (!File.Exists(assetPath))
                {
                    logger.LogWarning("Flow template: файли расми пешфарз ёфт нашуд: {AssetPath}", assetPath);
                    continue;
                }

                string attachmentId;
                await using (var stream = File.OpenRead(assetPath))
                    attachmentId = await instagramProvider.UploadMediaAsync(channel, stream, "image/png", templateNode.DefaultImageAsset, ct);

                var previewDataUri = await TryGenerateThumbnailDataUriAsync(assetPath, mediaProcessor, logger, ct);

                var config = JsonSerializer.Deserialize<MessageNodeConfig>(node.ConfigJson, FlowJsonOptions.Options)!;
                var blocks = config.Blocks.Append(new MessageBlock(MessageBlock.TypeImage, null, attachmentId, previewDataUri)).ToArray();
                node.ConfigJson = JsonSerializer.Serialize(config with { Blocks = blocks }, FlowJsonOptions.Options);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Flow template: боркунии расми пешфарз ноком шуд (Channel {ChannelId})", channel.Id);
            }
        }
    }

    /// <summary>128px JPEG хурд — ниг. FlowsEndpoints.TryGenerateThumbnailDataUriAsync (ҳамон
    /// мақсад, вале сарчашма аллакай файли диск аст, на IFormFile). Ноком шудан хатои сохтани
    /// flow-ро намебандад (caller-и AttachDefaultImagesAsync ҳамаи истисноҳоро catch мекунад).</summary>
    private static async Task<string?> TryGenerateThumbnailDataUriAsync(string assetPath, IMediaProcessor mediaProcessor, ILogger logger, CancellationToken ct)
    {
        var thumbPath = Path.Combine(Path.GetTempPath(), $"{Guid.CreateVersion7()}.jpg");
        try
        {
            await mediaProcessor.GenerateImageThumbnailAsync(assetPath, thumbPath, 128, ct);
            var bytes = await File.ReadAllBytesAsync(thumbPath, ct);
            return $"data:image/jpeg;base64,{Convert.ToBase64String(bytes)}";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flow template: сохтани thumbnail-и расми пешфарз ноком шуд: {AssetPath}", assetPath);
            return null;
        }
        finally
        {
            if (File.Exists(thumbPath)) File.Delete(thumbPath);
        }
    }
}
