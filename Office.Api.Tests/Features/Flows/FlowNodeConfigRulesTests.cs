using System.Text.Json;
using Office.Api.Channels.Flows;
using Office.Api.Features.Flows;

namespace Office.Api.Tests.Features.Flows;

public class FlowNodeConfigRulesTests
{
    private static string? Message(params MessageBlock[] blocks) =>
        FlowNodeConfigRules.Validate("message", JsonSerializer.SerializeToElement(new MessageNodeConfig(blocks, []), FlowJsonOptions.Options));

    private static MessageBlock Text(string text, string[]? variants = null) => new(MessageBlock.TypeText, text, null, Variants: variants);

    [Fact]
    public void AMessageSavedBeforeVariants_StillSaves()
    {
        var json = JsonDocument.Parse("""{"blocks":[{"type":"text","text":"Салом","mediaId":null}],"buttons":[]}""").RootElement;
        Assert.Null(FlowNodeConfigRules.Validate("message", json));
    }

    [Fact]
    public void Text_UpToInstagramsLimit()
    {
        Assert.Null(Message(Text(new string('a', MessageBlock.MaxTextLength))));
        Assert.NotNull(Message(Text(new string('a', MessageBlock.MaxTextLength + 1))));
    }

    [Fact]
    public void Variants_UpToFiveTextsInAll_EachWithinTheLimit()
    {
        Assert.Null(Message(Text("A", ["B", "C", "D", "E"])));
        Assert.NotNull(Message(Text("A", ["B", "C", "D", "E", "F"])));
        Assert.NotNull(Message(Text("A", [new string('b', MessageBlock.MaxTextLength + 1)])));
    }

    [Fact]
    public void Variants_OnlyOnText()
    {
        Assert.NotNull(Message(new MessageBlock(MessageBlock.TypeImage, null, "media-1", Variants: ["x"])));
    }
}
