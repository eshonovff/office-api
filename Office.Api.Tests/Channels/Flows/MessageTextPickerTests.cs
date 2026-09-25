using Office.Api.Channels.Flows;

namespace Office.Api.Tests.Channels.Flows;

public class MessageTextPickerTests
{
    private static MessageBlock Text(string text, params string[] variants) =>
        new(MessageBlock.TypeText, text, null, Variants: variants.Length == 0 ? null : variants);

    [Fact]
    public void WithoutVariants_TheTextItself()
    {
        Assert.Equal("A", MessageTextPicker.Pick(Text("A"), Guid.CreateVersion7(), Guid.CreateVersion7()));
    }

    [Fact]
    public void BlankVariants_AreIgnored()
    {
        Assert.Equal("A", MessageTextPicker.Pick(Text("A", "", "   "), Guid.CreateVersion7(), Guid.CreateVersion7()));
    }

    [Fact]
    public void SameContactAtTheSameNode_AlwaysTheSameText()
    {
        var block = Text("A", "B", "C");
        var session = Guid.CreateVersion7();
        var node = Guid.CreateVersion7();

        var first = MessageTextPicker.Pick(block, session, node);
        Assert.All(Enumerable.Range(0, 10), _ => Assert.Equal(first, MessageTextPicker.Pick(block, session, node)));
    }

    [Fact]
    public void AcrossContacts_EveryTextIsUsed_AboutEvenly()
    {
        var block = Text("A", "B", "C");
        var node = Guid.CreateVersion7();

        var counts = Enumerable.Range(0, 900)
            .Select(_ => MessageTextPicker.Pick(block, Guid.CreateVersion7(), node))
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        Assert.Equal(["A", "B", "C"], counts.Keys.Order());
        // 300 each on average; 200 is far outside chance for a fair pick.
        Assert.All(counts.Values, c => Assert.InRange(c, 200, 400));
    }
}
