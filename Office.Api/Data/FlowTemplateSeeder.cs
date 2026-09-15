using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Flows;
using Office.Api.Data.Entities;

namespace Office.Api.Data;

/// <summary>
/// Се шаблони аввалия барои марказҳои таълимӣ (спека). Идемпотентӣ — ниг. DbSeeder.SeedAsync:
/// агар ягон FlowTemplate аллакай мавҷуд бошад, дубора сабт намешавад.
/// </summary>
public static class FlowTemplateSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        if (await db.FlowTemplates.AnyAsync(ct))
            return;

        db.FlowTemplates.AddRange(
            new FlowTemplate
            {
                Id = Guid.CreateVersion7(),
                Name = "Лид-магнит бо тасдиқи обуна",
                Description = "Пеш аз фиристодани файл/линк, тафтиш мекунад ки корбар обуна ҳаст ё не.",
                DefinitionJson = JsonSerializer.Serialize(LeadMagnetTemplate()),
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new FlowTemplate
            {
                Id = Guid.CreateVersion7(),
                Name = "Ҷавоб ба комментарий + DM",
                Description = "Ҳамон автоматизатсияи оддии V1 — ҷавоб дар коментарий ва як паём дар DM.",
                DefinitionJson = JsonSerializer.Serialize(CommentReplyTemplate()),
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new FlowTemplate
            {
                Id = Guid.CreateVersion7(),
                Name = "Ҷамъоварии контакт",
                Description = "Ном → рақами телефон → тег — барои ҷамъоварии лидҳо тавассути DM.",
                DefinitionJson = JsonSerializer.Serialize(ContactCollectionTemplate()),
                CreatedAt = DateTimeOffset.UtcNow,
            });

        await db.SaveChangesAsync(ct);
    }

    private static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, FlowJsonOptions.Options);

    private static FlowTemplateDefinition LeadMagnetTemplate()
    {
        var condition = new FlowTemplateNodeDefinition("condition", "condition",
            ToElement(new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule(ConditionRule.FieldSubscription, "equals", "true")])),
            0, 0);
        var onFollowing = new FlowTemplateNodeDefinition("onFollowing", "message",
            ToElement(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Ташаккур барои обуна! Ана линки гайди шумо: [линкро ин ҷо гузоред]", null)], [])),
            300, -80);
        var onNotFollowing = new FlowTemplateNodeDefinition("onNotFollowing", "message",
            ToElement(new MessageNodeConfig(
                [new MessageBlock(MessageBlock.TypeText, "Барои гирифтани гайд, лутфан аввал ба аккаунти мо обуна шавед, баъд тугмаро пахш кунед.", null)],
                [new MessageButton("Ман обуна шудам", MessageButton.ActionNext, null, true)])),
            300, 80);

        return new FlowTemplateDefinition(
            [condition, onFollowing, onNotFollowing],
            [
                new FlowTemplateEdgeDefinition("condition", "match", "onFollowing"),
                new FlowTemplateEdgeDefinition("condition", "nomatch", "onNotFollowing"),
                // Тугмаи "Ман обуна шудам" ба ҳамон шарт бармегардад — то боз санҷида шавад.
                new FlowTemplateEdgeDefinition("onNotFollowing", "button:0", "condition"),
            ]);
    }

    private static FlowTemplateDefinition CommentReplyTemplate()
    {
        var message = new FlowTemplateNodeDefinition("message", "message",
            ToElement(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Ташаккур барои саволатон! DM-ро тафтиш кунед.", null)], [])),
            0, 0);

        return new FlowTemplateDefinition([message], []);
    }

    private static FlowTemplateDefinition ContactCollectionTemplate()
    {
        var askName = new FlowTemplateNodeDefinition("askName", "message",
            ToElement(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом! Номи шумо чист?", null)], [])), 0, 0);
        var collectName = new FlowTemplateNodeDefinition("collectName", "action",
            ToElement(new ActionNodeConfig(ActionNodeConfig.KindCollectInput, VariableKey: "name")), 250, 0);
        var askPhone = new FlowTemplateNodeDefinition("askPhone", "message",
            ToElement(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Ташаккур, {{name}}! Рақами телефонатонро нависед.", null)], [])), 500, 0);
        var collectPhone = new FlowTemplateNodeDefinition("collectPhone", "action",
            ToElement(new ActionNodeConfig(ActionNodeConfig.KindCollectInput, VariableKey: "phone")), 750, 0);
        var addTag = new FlowTemplateNodeDefinition("addTag", "action",
            ToElement(new ActionNodeConfig(ActionNodeConfig.KindAddTags, Tags: ["lead_collected"])), 1000, 0);
        var thanks = new FlowTemplateNodeDefinition("thanks", "message",
            ToElement(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Ташаккур! Мо бо шумо тез тамос мегирем.", null)], [])), 1250, 0);

        return new FlowTemplateDefinition(
            [askName, collectName, askPhone, collectPhone, addTag, thanks],
            [
                new FlowTemplateEdgeDefinition("askName", "default", "collectName"),
                new FlowTemplateEdgeDefinition("collectName", "default", "askPhone"),
                new FlowTemplateEdgeDefinition("askPhone", "default", "collectPhone"),
                new FlowTemplateEdgeDefinition("collectPhone", "default", "addTag"),
                new FlowTemplateEdgeDefinition("addTag", "default", "thanks"),
            ]);
    }
}
