using Office.Api.Channels;

namespace Office.Api.Tests.Channels;

public class FailureCodeLabelsTests
{
    [Theory]
    [InlineData("FB_100_2018074")]
    [InlineData("IG_2")]
    [InlineData("WA_131030")]
    [InlineData("FB_190")]
    [InlineData("IG_131047")]
    public void IsKnown_MappedCode_ReturnsTrue(string failureCode)
    {
        Assert.True(FailureCodeLabels.IsKnown(failureCode));
    }

    [Theory]
    [InlineData("IG_1")] // А2: як маротиба дида шудааст — қасдан ношинос мондааст
    [InlineData("FB_999")]
    [InlineData("WA_1")]
    public void IsKnown_UnmappedCode_ReturnsFalse(string failureCode)
    {
        Assert.False(FailureCodeLabels.IsKnown(failureCode));
    }

    [Fact]
    public void Label_UnknownCode_ReturnsTheCodeItselfNotAGenericMessage()
    {
        Assert.Equal("IG_1", FailureCodeLabels.Label("IG_1"));
    }
}
