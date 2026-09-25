using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Flows;
using Office.Api.Data.Entities;

namespace Office.Api.Data;

/// <summary>
/// Шаблонҳои аввалия барои марказҳои таълимӣ (спека). Идемпотентӣ БО НОМ (на "агар ҷадвал холӣ
/// бошад") — то навсозии DefinitionJson-и як шаблон (масалан LeadMagnetTemplate такмил ёфт)
/// воқеан ба муштариёне, ки аллакай як бор seed шудаанд (production), низ расад. Flow-ҳое, ки
/// корбар аллакай аз рӯи шаблони кӯҳна сохтааст, бетаъсир мемонанд — DefinitionJson танҳо дар
/// лаҳзаи "Сохтан аз шаблон" истифода мешавад, на пас аз он нигоҳ дошта.
/// </summary>
public static class FlowTemplateSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct)
    {
        await UpsertAsync(db, "Лид-магнит бо тасдиқи обуна",
            "Пеш аз фиристодани файл/линк, тафтиш мекунад ки корбар обуна ҳаст ё не.", LeadMagnetTemplate(), ct);
        await UpsertAsync(db, "Ҷавоб ба комментарий + DM",
            // Shown to мизоҷон too — plain words, no internal names (it used to mention automation_rules).
            "Ба ҳар касе, ки коментарий ё паём менависад, дар Direct ҷавоб мефиристад. Ҷавоб зери худи пост — дар саҳифаи «Шарҳҳо» (автоҷавоб).",
            CommentReplyTemplate(), ct);
        await UpsertAsync(db, "Ҷамъоварии контакт",
            "Ном → рақами телефон → тег — барои ҷамъоварии лидҳо тавассути DM.", ContactCollectionTemplate(), ct);

        // Withdrawn 2026-09-25: the follow check for comments lives in the comment auto-reply
        // (comments page), as for staff. Flows already made from it are the мизоҷ's and stay.
        await RemoveAsync(db, "Ҷавоб ба шарҳ бо санҷиши обуна", ct);

        await db.SaveChangesAsync(ct);
    }

    private static async Task RemoveAsync(AppDbContext db, string name, CancellationToken ct)
    {
        var existing = await db.FlowTemplates.FirstOrDefaultAsync(t => t.Name == name, ct);
        if (existing is not null)
            db.FlowTemplates.Remove(existing);
    }

    private static async Task UpsertAsync(AppDbContext db, string name, string description, FlowTemplateDefinition definition, CancellationToken ct)
    {
        var definitionJson = JsonSerializer.Serialize(definition);
        var existing = await db.FlowTemplates.FirstOrDefaultAsync(t => t.Name == name, ct);
        if (existing is not null)
        {
            existing.Description = description;
            existing.DefinitionJson = definitionJson;
            return;
        }

        db.FlowTemplates.Add(new FlowTemplate
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Description = description,
            DefinitionJson = definitionJson,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private static JsonElement ToElement<T>(T value) => JsonSerializer.SerializeToElement(value, FlowJsonOptions.Options);

    /// <summary>
    /// Такмилёфта (2026-09-17, аз рӯи мисоли зиндаи ChatPlace): пеш аз санҷиши обуна, паёми
    /// оғозин бо тугма — то фақат корбари воқеан хоҳишманд идома ёбад, на ҳар кас (ниг. тег
    /// "wants_guide"-ро барои филтр/CRM). Хусусияти "ёдоварии худкор пас аз N дақиқаи хомӯшӣ"-и
    /// мисоли ChatPlace (шоха бе алоқа ба идомаи асосӣ) КИРО НАШУДААСТ — FlowEngine танҳо ЯК
    /// роҳро дар як сессия дунбол мекунад (edges.FirstOrDefault барои ҳар порт), пас ду шохаи
    /// мустақили ҳамзамон аз як нод сохта намешавад. Ниг. ҷавоби чат барои тафсил.
    /// </summary>
    private static FlowTemplateDefinition LeadMagnetTemplate()
    {
        var intro = new FlowTemplateNodeDefinition("intro", "message",
            ToElement(new MessageNodeConfig(
                [new MessageBlock(MessageBlock.TypeText, "Салом, {{firstName}}! Мехоҳед видеогайди ройгонро бинед? 🔥", null)],
                [new MessageButton("Ҳа, мехоҳам!", MessageButton.ActionNext, null, false)])),
            0, 0, DefaultImageAsset: "lead-magnet.png");
        var tagWants = new FlowTemplateNodeDefinition("tagWants", "action",
            ToElement(new ActionNodeConfig(ActionNodeConfig.KindAddTags, Tags: ["wants_guide"])),
            300, 0);
        var condition = new FlowTemplateNodeDefinition("condition", "condition",
            ToElement(new ConditionNodeConfig(ConditionNodeConfig.MatchAll, [new ConditionRule(ConditionRule.FieldSubscription, "equals", "true")])),
            600, 0);
        var onFollowing = new FlowTemplateNodeDefinition("onFollowing", "message",
            ToElement(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Ташаккур барои обуна! Ана линки гайди шумо: [линкро ин ҷо гузоред]", null)], [])),
            900, -80);
        var onNotFollowing = new FlowTemplateNodeDefinition("onNotFollowing", "message",
            ToElement(new MessageNodeConfig(
                [new MessageBlock(MessageBlock.TypeText, "Барои гирифтани гайд, лутфан аввал ба аккаунти мо обуна шавед, баъд тугмаро пахш кунед.", null)],
                [new MessageButton("Тайёр ✅", MessageButton.ActionNext, null, true)])),
            900, 80);

        return new FlowTemplateDefinition(
            [intro, tagWants, condition, onFollowing, onNotFollowing],
            [
                new FlowTemplateEdgeDefinition("intro", "button:0", "tagWants"),
                new FlowTemplateEdgeDefinition("tagWants", "default", "condition"),
                new FlowTemplateEdgeDefinition("condition", "match", "onFollowing"),
                new FlowTemplateEdgeDefinition("condition", "nomatch", "onNotFollowing"),
                // Тугмаи "Тайёр ✅" ба ҳамон шарт бармегардад — то боз санҷида шавад.
                new FlowTemplateEdgeDefinition("onNotFollowing", "button:0", "condition"),
            ]);
    }

    /// <summary>
    /// Матни паём қасдан ба "DM-ро тафтиш кунед" ишора намекунад — Flow ҳеҷ гоҳ дар худи
    /// коментарий ҷавоби ҷамъиятӣ намефиристад (танҳо ин паёми DM), пас чунин ишора ба чизе,
    /// ки корбар намебинад, гумроҳкунанда буд. Паём худаш бояд пурра бошад.
    /// </summary>
    private static FlowTemplateDefinition CommentReplyTemplate()
    {
        var message = new FlowTemplateNodeDefinition("message", "message",
            // Three wordings — each commenter gets one (MessageTextPicker): the same text to everyone looks like spam.
            ToElement(new MessageNodeConfig([new MessageBlock(MessageBlock.TypeText, "Салом, {{firstName}}! Ташаккур барои таваҷҷуҳатон 🙌 Мо ба зудӣ бо шумо тамос мегирем.", null,
                Variants:
                [
                    "Салом, {{firstName}}! Саволатонро гирифтем 😊 Ба зудӣ ҷавоб медиҳем.",
                    "{{firstName}}, ташаккур барои шарҳ! 🙏 Мутахассиси мо ба зудӣ ба шумо менависад.",
                ])], [])),
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
