using Microsoft.EntityFrameworkCore;
using Office.Api.Channels;
using Office.Api.Channels.ContactProfiles;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Channels.ContactProfiles;

public class ContactProfileUpdaterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly Channel _ig = new() { Id = Guid.NewGuid(), Type = ChannelType.Instagram, Name = "a", ExternalId = "ig-a", IsActive = true };
    private readonly Channel _otherIg = new() { Id = Guid.NewGuid(), Type = ChannelType.Instagram, Name = "b", ExternalId = "ig-b", IsActive = true };

    private Conversation Chat(Channel channel, string? name = null, string? username = null) =>
        new() { Id = Guid.NewGuid(), ChannelId = channel.Id, ExternalId = "fan-1", ContactName = name, ContactUsername = username };

    private void Comment(Channel channel, string author, string username, int minutesAgo) => _db.InstagramComments.Add(new InstagramComment
    {
        Id = Guid.NewGuid(), ChannelId = channel.Id, ExternalId = Guid.NewGuid().ToString(), MediaExternalId = "m1",
        AuthorExternalId = author, AuthorUsername = username, Text = "нарх?", CommentedAt = Now.AddMinutes(-minutesAgo), ReceivedAt = Now,
    });

    [Fact]
    public async Task WhatMetaSaysNow_IsWritten_AndThePictureIsToBeFetched()
    {
        var chat = Chat(_ig, name: "Old name");
        var provider = new FakeProfileProvider(_ => new ContactProfile("Mir777 naja", "https://scontent.cdninstagram.com/p.jpg", "mir777naja"));

        var picture = await ContactProfileUpdater.RefreshAsync(_db, provider, _ig, chat, Now, CancellationToken.None);

        Assert.True(picture);
        Assert.Equal(("Mir777 naja", "mir777naja", "https://scontent.cdninstagram.com/p.jpg", (DateTimeOffset?)Now),
            (chat.ContactName, chat.ContactUsername, chat.ContactAvatarUrl, chat.ContactProfileFetchedAt));
    }

    [Fact]
    public async Task WhenMetaSaysNothing_NothingIsErased_ButTheAskIsNoted()
    {
        var chat = Chat(_ig, name: "Fan", username: "fan");

        var picture = await ContactProfileUpdater.RefreshAsync(_db, new FakeProfileProvider(), _ig, chat, Now, CancellationToken.None);

        Assert.False(picture);
        Assert.Equal(("Fan", "fan", (DateTimeOffset?)Now), (chat.ContactName, chat.ContactUsername, chat.ContactProfileFetchedAt));
    }

    [Fact]
    public async Task AFanOnlyKnownByTheirComment_IsNamedByIt_OnlyFromThisAccount()
    {
        // The auto-reply wrote first; Meta says nothing yet — the comment on THIS account says who.
        _db.Channels.AddRange(_ig, _otherIg);
        Comment(_otherIg, "fan-1", "someone_elses_fan", 1); // same id on another account: never used
        Comment(_ig, "fan-1", "old_name", 60);
        Comment(_ig, "fan-1", "farzona.style", 5);
        await _db.SaveChangesAsync();
        var chat = Chat(_ig);

        await ContactProfileUpdater.RefreshAsync(_db, new FakeProfileProvider(), _ig, chat, Now, CancellationToken.None);

        Assert.Equal(("farzona.style", "farzona.style"), (chat.ContactUsername, chat.ContactName));
    }
}
