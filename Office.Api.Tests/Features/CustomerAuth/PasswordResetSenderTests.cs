using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Email;
using Office.Api.Features.CustomerAuth;

namespace Office.Api.Tests.Features.CustomerAuth;

public class PasswordResetSenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _db =
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly RecordingEmailSender _email = new();

    private sealed class RecordingEmailSender : IEmailSender
    {
        public List<(string To, string Subject, string Body)> Sent { get; } = [];

        public Task<bool> SendAsync(string to, string subject, string bodyText, CancellationToken ct)
        {
            Sent.Add((to, subject, bodyText));
            return Task.FromResult(true);
        }
    }

    private PasswordResetSender Sender() => new(
        _db,
        _email,
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["App:PublicUrl"] = "https://office.nizom.tj/" })
            .Build(),
        NullLogger<PasswordResetSender>.Instance);

    private Customer AddCustomer(string email, bool verified = true, bool active = true, string? passwordHash = "hash")
    {
        var customer = new Customer
        {
            Id = Guid.NewGuid(), Email = email, FullName = "Мизоҷ", PasswordHash = passwordHash,
            EmailVerifiedAt = verified ? Now.AddDays(-3) : null, IsActive = active, CreatedAt = Now.AddDays(-3),
        };
        _db.Customers.Add(customer);
        _db.SaveChanges();
        return customer;
    }

    private static string TokenFrom(string body) =>
        Regex.Match(body, @"https://office\.nizom\.tj/reset-password#token=([A-Za-z0-9_-]+)").Groups[1].Value;

    [Fact]
    public async Task VerifiedAccount_GetsALink_AndOnlyTheTokensHashIsStored()
    {
        var customer = AddCustomer("a@example.com");

        var result = await Sender().SendAsync("a@example.com", Now, CancellationToken.None);

        Assert.Equal(PasswordResetSendResult.Sent, result);
        var (to, _, body) = Assert.Single(_email.Sent);
        Assert.Equal("a@example.com", to);
        var token = TokenFrom(body);
        Assert.Equal(43, token.Length); // the configured address, trailing slash trimmed, token in the fragment

        var row = await _db.CustomerPasswordResets.SingleAsync();
        Assert.Equal(customer.Id, row.CustomerId);
        Assert.Equal(PasswordResetRules.Hash(token), row.TokenHash);
        Assert.NotEqual(token, row.TokenHash);
        Assert.Equal(Now.AddMinutes(30), row.ExpiresAt);
        Assert.Null(row.ConsumedAt);
    }

    [Theory]
    [InlineData("nobody@example.com", true, true)] // no such account
    [InlineData("a@example.com", false, true)] // never proved it owns the email
    [InlineData("a@example.com", true, false)] // switched off
    public async Task NoLink_ForUnknownUnverifiedOrInactive(string email, bool verified, bool active)
    {
        AddCustomer("a@example.com", verified, active);

        var result = await Sender().SendAsync(email, Now, CancellationToken.None);

        Assert.Equal(PasswordResetSendResult.NoAccount, result);
        Assert.Empty(_email.Sent);
        Assert.Empty(await _db.CustomerPasswordResets.ToListAsync());
    }

    [Fact]
    public async Task GoogleOnlyAccount_CanSetAPassword()
    {
        AddCustomer("g@example.com", passwordHash: null);

        Assert.Equal(PasswordResetSendResult.Sent, await Sender().SendAsync("g@example.com", Now, CancellationToken.None));
    }

    [Fact]
    public async Task ASecondRequestWithinAMinute_SendsNothing()
    {
        AddCustomer("a@example.com");
        await Sender().SendAsync("a@example.com", Now, CancellationToken.None);

        var result = await Sender().SendAsync("a@example.com", Now.AddSeconds(30), CancellationToken.None);

        Assert.Equal(PasswordResetSendResult.CoolingDown, result);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task ANewLink_VoidsTheEarlierOne()
    {
        AddCustomer("a@example.com");
        await Sender().SendAsync("a@example.com", Now, CancellationToken.None);
        var firstHash = PasswordResetRules.Hash(TokenFrom(_email.Sent[0].Body));

        await Sender().SendAsync("a@example.com", Now.AddMinutes(2), CancellationToken.None);

        var first = await _db.CustomerPasswordResets.SingleAsync(r => r.TokenHash == firstHash);
        Assert.Equal(Now.AddMinutes(2), first.ConsumedAt);
        Assert.Single(await _db.CustomerPasswordResets.Where(r => r.ConsumedAt == null).ToListAsync());
    }

    [Fact]
    public async Task AtMostFiveLinksADay_ThenAgainTheNextDay()
    {
        AddCustomer("a@example.com");
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(PasswordResetSendResult.Sent,
                await Sender().SendAsync("a@example.com", Now.AddMinutes(i * 2), CancellationToken.None));
        }

        var sixth = await Sender().SendAsync("a@example.com", Now.AddMinutes(20), CancellationToken.None);

        Assert.Equal(PasswordResetSendResult.DailyCapReached, sixth);
        Assert.Equal(5, _email.Sent.Count);
        // A day later it works again, and the old rows are cleaned away.
        Assert.Equal(PasswordResetSendResult.Sent,
            await Sender().SendAsync("a@example.com", Now.AddHours(25), CancellationToken.None));
        Assert.Single(await _db.CustomerPasswordResets.ToListAsync());
    }

    [Fact]
    public async Task OnlyThatAccountsLinksAreTouched()
    {
        AddCustomer("a@example.com");
        AddCustomer("b@example.com");
        await Sender().SendAsync("b@example.com", Now, CancellationToken.None);

        await Sender().SendAsync("a@example.com", Now.AddMinutes(2), CancellationToken.None);

        Assert.Equal(2, await _db.CustomerPasswordResets.CountAsync(r => r.ConsumedAt == null));
    }
}
