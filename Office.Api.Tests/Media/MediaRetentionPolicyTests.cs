using Office.Api.Data.Entities;
using Office.Api.Media;

namespace Office.Api.Tests.Media;

public class MediaRetentionPolicyTests
{
    private static readonly MediaRetentionOptions DefaultOptions = new(ImageDays: 365, VoiceDays: 365, DocumentDays: 365, VideoDays: 7);
    private static readonly DateTimeOffset Now = new(2026, 8, 10, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(MessageType.Image)]
    [InlineData(MessageType.Audio)]
    [InlineData(MessageType.File)]
    public void IsExpired_OneYearRetentionType_JustUnder365Days_ReturnsFalse(MessageType type)
    {
        var createdAt = Now.AddDays(-364);
        Assert.False(MediaRetentionPolicy.IsExpired(type, createdAt, Now, DefaultOptions));
    }

    [Theory]
    [InlineData(MessageType.Image)]
    [InlineData(MessageType.Audio)]
    [InlineData(MessageType.File)]
    public void IsExpired_OneYearRetentionType_Exactly365Days_ReturnsTrue(MessageType type)
    {
        var createdAt = Now.AddDays(-365);
        Assert.True(MediaRetentionPolicy.IsExpired(type, createdAt, Now, DefaultOptions));
    }

    [Fact]
    public void IsExpired_Video_After7Days_ReturnsTrue()
    {
        var createdAt = Now.AddDays(-8);
        Assert.True(MediaRetentionPolicy.IsExpired(MessageType.Video, createdAt, Now, DefaultOptions));
    }

    [Fact]
    public void IsExpired_Video_Under7Days_ReturnsFalse()
    {
        var createdAt = Now.AddDays(-6);
        Assert.False(MediaRetentionPolicy.IsExpired(MessageType.Video, createdAt, Now, DefaultOptions));
    }

    [Theory]
    [InlineData(MessageType.Text)]
    [InlineData(MessageType.Location)]
    [InlineData(MessageType.Contact)]
    [InlineData(MessageType.StoryReply)]
    public void IsExpired_NonMediaType_NeverExpires(MessageType type)
    {
        var createdAt = Now.AddYears(-10);
        Assert.False(MediaRetentionPolicy.IsExpired(type, createdAt, Now, DefaultOptions));
    }

    [Fact]
    public void IsExpired_ZeroRetentionDays_NeverExpires()
    {
        var options = new MediaRetentionOptions(ImageDays: 0, VoiceDays: 0, DocumentDays: 0, VideoDays: 0);
        var createdAt = Now.AddYears(-10);
        Assert.False(MediaRetentionPolicy.IsExpired(MessageType.Image, createdAt, Now, options));
    }
}
