using Office.Api.Channels.ContactProfiles;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels.ContactProfiles;

public class ContactProfilePolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    private static Conversation Chat(string? username = null, string? name = null, string? picture = null, TimeSpan? askedAgo = null) => new()
    {
        ExternalId = "fan",
        ContactUsername = username,
        ContactName = name,
        ContactAvatarPath = picture,
        ContactProfileFetchedAt = askedAgo is null ? null : Now - askedAgo.Value,
    };

    [Fact]
    public void NeverAsked_IsAsked()
    {
        Assert.True(ContactProfilePolicy.ShouldAskOnMessage(Chat(), ChannelType.Instagram, Now));
    }

    [Fact]
    public void Unknown_AskedAgainOnTheirMessage_ButNotMoreThanOnceAMinute()
    {
        // The account wrote first: asked when the chat was made, Meta said nothing. Their reply is the moment.
        Assert.False(ContactProfilePolicy.ShouldAskOnMessage(Chat(askedAgo: TimeSpan.FromSeconds(30)), ChannelType.Instagram, Now));
        Assert.True(ContactProfilePolicy.ShouldAskOnMessage(Chat(askedAgo: TimeSpan.FromMinutes(2)), ChannelType.Instagram, Now));
    }

    [Fact]
    public void Known_WithoutAPicture_AskedOnceADay()
    {
        Assert.False(ContactProfilePolicy.ShouldAskOnMessage(Chat("fan", "Fan", askedAgo: TimeSpan.FromHours(2)), ChannelType.Instagram, Now));
        Assert.True(ContactProfilePolicy.ShouldAskOnMessage(Chat("fan", "Fan", askedAgo: TimeSpan.FromHours(25)), ChannelType.Instagram, Now));
    }

    [Fact]
    public void Known_WithAPicture_RefreshedWeekly()
    {
        Assert.False(ContactProfilePolicy.ShouldAskOnMessage(Chat("fan", "Fan", "p.jpg", TimeSpan.FromDays(6)), ChannelType.Instagram, Now));
        Assert.True(ContactProfilePolicy.ShouldAskOnMessage(Chat("fan", "Fan", "p.jpg", TimeSpan.FromDays(8)), ChannelType.Instagram, Now));
    }

    [Fact]
    public void Facebook_IsKnownByName_WhatsApp_NeverAsked()
    {
        Assert.False(ContactProfilePolicy.IsUnknown(Chat(name: "Ali"), ChannelType.Facebook)); // Facebook has no @username
        Assert.True(ContactProfilePolicy.IsUnknown(Chat(name: "Ali"), ChannelType.Instagram));
        Assert.False(ContactProfilePolicy.ShouldAskOnMessage(Chat(), ChannelType.WhatsApp, Now)); // its webhook names the person
    }
}
