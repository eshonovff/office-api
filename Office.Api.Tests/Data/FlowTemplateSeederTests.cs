using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Office.Api.Channels.Flows;
using Office.Api.Data;

namespace Office.Api.Tests.Data;

/// <summary>
/// Санҷиши регрессия барои хатои production-и 2026-09-15: FlowTemplateSeeder бо
/// JsonSerializer.SerializeToElement-и БЕПАРАМЕТР (PascalCase-и пешфарз) config_json месохт,
/// дар ҳоле ки frontend камелCase-ро интизор аст (FlowNodeDto.Config — JsonElement-и хом, бе
/// табдили ASP.NET-и camelCase-и худкор). Натиҷа: canvas бо "config.rules is undefined" афтод.
/// Ҳал: FlowJsonOptions.Options (JsonSerializerDefaults.Web) дар ҳама ҷо.
/// </summary>
public class FlowTemplateSeederTests
{
    private static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    [Fact]
    public async Task SeedAsync_ProducesCamelCaseNodeConfigJson_ForEveryTemplate()
    {
        await using var db = CreateDb();
        await FlowTemplateSeeder.SeedAsync(db, CancellationToken.None);

        var templates = await db.FlowTemplates.ToListAsync();
        Assert.Equal(3, templates.Count);

        foreach (var template in templates)
        {
            var definition = JsonSerializer.Deserialize<FlowTemplateDefinition>(template.DefinitionJson, FlowJsonOptions.Options)!;
            Assert.NotEmpty(definition.Nodes);

            foreach (var node in definition.Nodes)
            {
                var keys = node.Config.EnumerateObject().Select(p => p.Name).ToList();
                Assert.All(keys, key => Assert.Equal(char.ToLowerInvariant(key[0]), key[0]));
            }
        }
    }

    [Fact]
    public async Task SeedAsync_ConditionNodeConfig_HasLowercaseRulesKey_NotPascalCase()
    {
        await using var db = CreateDb();
        await FlowTemplateSeeder.SeedAsync(db, CancellationToken.None);

        var leadMagnet = await db.FlowTemplates.FirstAsync(t => t.Name.Contains("обуна"));
        var definition = JsonSerializer.Deserialize<FlowTemplateDefinition>(leadMagnet.DefinitionJson, FlowJsonOptions.Options)!;
        var condition = definition.Nodes.Single(n => n.Type == "condition");

        Assert.True(condition.Config.TryGetProperty("rules", out _));
        Assert.False(condition.Config.TryGetProperty("Rules", out _));
    }

    [Fact]
    public async Task SeedAsync_MessageNodeConfig_HasLowercaseBlocksAndButtonsKeys()
    {
        await using var db = CreateDb();
        await FlowTemplateSeeder.SeedAsync(db, CancellationToken.None);

        var contactCollection = await db.FlowTemplates.FirstAsync(t => t.Name.Contains("контакт"));
        var definition = JsonSerializer.Deserialize<FlowTemplateDefinition>(contactCollection.DefinitionJson, FlowJsonOptions.Options)!;
        var message = definition.Nodes.First(n => n.Type == "message");

        Assert.True(message.Config.TryGetProperty("blocks", out _));
        Assert.True(message.Config.TryGetProperty("buttons", out _));
    }

    /// <summary>
    /// Регрессия: то ин тағйирот SeedAsync танҳо "агар ҷадвал холӣ бошад" кор мекард — дар DB-и
    /// аллакай seed-шуда (production) навсозии LeadMagnetTemplate ҳеҷ гоҳ намерасид. Ҳоло бо
    /// ном upsert мешавад — иҷрои дуюм (мисли restart-и сервери воқеӣ) бояд DefinitionJson-ро
    /// нав кунад, на нодида гирад, ва шумораи умумии шаблонҳоро дучанд накунад.
    /// </summary>
    [Fact]
    public async Task SeedAsync_CalledTwice_UpdatesDefinitionInPlace_WithoutDuplicating()
    {
        await using var db = CreateDb();
        await FlowTemplateSeeder.SeedAsync(db, CancellationToken.None);
        var original = await db.FlowTemplates.FirstAsync(t => t.Name.Contains("обуна"));
        var originalId = original.Id;

        await FlowTemplateSeeder.SeedAsync(db, CancellationToken.None);

        var all = await db.FlowTemplates.Where(t => t.Name.Contains("обуна")).ToListAsync();
        var updated = Assert.Single(all);
        Assert.Equal(originalId, updated.Id);

        var definition = JsonSerializer.Deserialize<FlowTemplateDefinition>(updated.DefinitionJson, FlowJsonOptions.Options)!;
        Assert.Contains(definition.Nodes, n => n.Key == "intro");
    }
}
