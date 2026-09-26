using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Office.Api.Auth;
using Office.Api.Channels.ContactProfiles;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;
using Office.Api.Features.CustomerChats;
using Office.Api.Features.CustomerContacts;

namespace Office.Api.Tests.Features;

/// <summary>
/// Who may see a contact's picture: a мизоҷ only their own contacts' (tenant filter — B's and the
/// company's are "not found"); staff only company chats they may open (and never a мизоҷ's);
/// the apps get our link, never Meta's; deleting a contact deletes its picture.
/// </summary>
public sealed class ContactAvatarEndpointsTests : IDisposable
{
    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private sealed class TestEnvironment(string root) : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = root;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Office.Api";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = root;
        public string EnvironmentName { get; set; } = "Test";
    }

    /// <summary>Staff may open chats of these channels only.</summary>
    private sealed class Guard(params Guid[] allowed) : IChannelAccessGuard
    {
        public Task<IQueryable<Conversation>> ApplyAccessFilterAsync(IQueryable<Conversation> query, ClaimsPrincipal principal, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, Guid channelId, Guid? assignedTo, CancellationToken ct) =>
            Task.FromResult(allowed.Contains(channelId));
        public Task<(IQueryable<Channel> Query, bool Joinable)> ApplyChannelAccessFilterAsync(IQueryable<Channel> query, ClaimsPrincipal principal, CancellationToken ct) =>
            throw new NotSupportedException();
        public Task<bool> CanAccessChannelAsync(ClaimsPrincipal principal, Guid channelId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> CanUserBeAssignedToChannelAsync(Guid userId, Guid channelId, CancellationToken ct) => throw new NotSupportedException();
        public IQueryable<User> ApplyAssignableUsersFilter(IQueryable<User> query, Guid channelId) => throw new NotSupportedException();
    }

    private readonly string _name = Guid.NewGuid().ToString();
    private readonly string _root = Directory.CreateTempSubdirectory("avatar-endpoints-").FullName;
    private readonly IConfiguration _configuration;
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _customerB = Guid.NewGuid();
    private readonly Guid _channelA = Guid.NewGuid();
    private readonly Guid _channelB = Guid.NewGuid();
    private readonly Guid _company = Guid.NewGuid();
    private readonly Guid _companyOther = Guid.NewGuid();
    private readonly Guid _a1 = Guid.NewGuid();
    private readonly Guid _b1 = Guid.NewGuid();
    private readonly Guid _c1 = Guid.NewGuid();
    private readonly Guid _c2 = Guid.NewGuid();
    private readonly Guid _noPicture = Guid.NewGuid();

    public ContactAvatarEndpointsTests()
    {
        _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Uploads:RootPath"] = _root }).Build();
        using var db = Open(null);
        db.Customers.AddRange(
            new Customer { Id = _customerA, Email = "a@example.com", FullName = "A", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) },
            new Customer { Id = _customerB, Email = "b@example.com", FullName = "B", TrialEndsAt = DateTimeOffset.UtcNow.AddDays(5) });
        db.Channels.AddRange(Channel(_channelA, _customerA), Channel(_channelB, _customerB), Channel(_company, null), Channel(_companyOther, null));
        db.Conversations.AddRange(Chat(_a1, _channelA), Chat(_b1, _channelB), Chat(_c1, _company), Chat(_c2, _companyOther),
            new Conversation { Id = _noPicture, ChannelId = _channelA, ExternalId = "fan-x", CreatedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static Channel Channel(Guid id, Guid? owner) =>
        new() { Id = id, Type = ChannelType.Instagram, Name = "ig", ExternalId = id.ToString(), CustomerId = owner, IsActive = true };

    private Conversation Chat(Guid id, Guid channelId)
    {
        var path = Path.Combine("whatsapp-media", channelId.ToString(), "avatars", $"{id}-1.jpg");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_root, path))!);
        File.WriteAllBytes(Path.Combine(_root, path), [0xFF, 0xD8, 0xFF, 0xD9]);
        return new Conversation
        {
            Id = id, ChannelId = channelId, ExternalId = "fan-1", ContactName = "Fan", ContactUsername = "fan",
            ContactAvatarUrl = "https://scontent.cdninstagram.com/expired.jpg", ContactAvatarPath = path, CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private AppDbContext AsA() => Open(new TenantIdentity(TenantScope.Customer, _customerA));
    private AppDbContext AsStaff() => Open(new TenantIdentity(TenantScope.Staff, null));
    private static ClaimsPrincipal Someone(Guid id) => new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, id.ToString())], "test"));
    private static int? Status(IResult result) => (result as IStatusCodeHttpResult)?.StatusCode;

    private async Task<IResult> CustomerAvatar(Guid chat)
    {
        await using var db = AsA();
        return await CustomerChatsEndpoints.AvatarAsync(chat, db, new DefaultHttpContext(), _configuration, new TestEnvironment(_root), CancellationToken.None);
    }

    private async Task<(IResult Result, HttpContext Http)> StaffAvatar(Guid chat, params Guid[] allowed)
    {
        await using var db = AsStaff();
        var http = new DefaultHttpContext();
        var result = await ConversationsEndpoints.AvatarAsync(
            chat, Someone(Guid.NewGuid()), db, new Guard(allowed), http, _configuration, new TestEnvironment(_root), CancellationToken.None);
        return (result, http);
    }

    [Fact]
    public async Task AMizoj_SeesTheirOwnContactsPicture()
    {
        var result = Assert.IsType<PhysicalFileHttpResult>(await CustomerAvatar(_a1));
        Assert.Equal("image/jpeg", result.ContentType);
    }

    [Fact]
    public async Task AMizoj_GetsNothingOfAnothersOrTheCompanys()
    {
        Assert.Equal(404, Status(await CustomerAvatar(_b1)));
        Assert.Equal(404, Status(await CustomerAvatar(_c1)));
        Assert.Equal(404, Status(await CustomerAvatar(_noPicture)));
    }

    [Fact]
    public async Task Staff_SeeAPictureOnlyOfACompanyChatTheyMayOpen_NeverAMizojs()
    {
        var (allowed, http) = await StaffAvatar(_c1, _company);
        Assert.IsType<PhysicalFileHttpResult>(allowed);
        Assert.Equal("nosniff", http.Response.Headers.XContentTypeOptions.ToString());
        Assert.StartsWith("private", http.Response.Headers.CacheControl.ToString());

        Assert.Equal(404, Status((await StaffAvatar(_c2, _company)).Result)); // no access to that channel
        Assert.Equal(404, Status((await StaffAvatar(_a1, _company, _channelA)).Result)); // a мизоҷ's — staff filter hides it
    }

    [Fact]
    public async Task TheAppsGetOurLink_NeverMetasExpiringOne()
    {
        var staffLink = ContactAvatarFiles.StaffLink(_c1, $"whatsapp-media/{_company}/avatars/{_c1}-1.jpg");
        Assert.Equal($"/api/conversations/{_c1}/avatar?v={_c1}-1", staffLink);
        Assert.Null(ContactAvatarFiles.CustomerLink(_a1, null));

        await using var db = AsA();
        var list = Assert.IsType<Ok<PagedResult<CustomerContactListItem>>>(
            await CustomerContactsEndpoints.ListAsync(null, null, null, null, null, null, db, CancellationToken.None)).Value!;
        var mine = list.Items.Single(c => c.Id == _a1);
        Assert.Equal($"/api/public/conversations/{_a1}/avatar?v={_a1}-1", mine.AvatarUrl);
        Assert.DoesNotContain(list.Items, c => c.AvatarUrl?.Contains("cdninstagram") == true);
    }

    [Fact]
    public async Task DeletingAContact_DeletesItsPicture()
    {
        var picture = Path.Combine(_root, "whatsapp-media", _channelA.ToString(), "avatars", $"{_a1}-1.jpg");
        Assert.True(File.Exists(picture));

        await using var db = AsA();
        var result = await CustomerContactsEndpoints.DeleteAsync(
            _a1, Someone(_customerA), db, _configuration, new TestEnvironment(_root), NullLogger<Program>.Instance, CancellationToken.None);

        Assert.Equal(204, Status(result));
        Assert.False(File.Exists(picture));
        Assert.True(File.Exists(Path.Combine(_root, "whatsapp-media", _channelB.ToString(), "avatars", $"{_b1}-1.jpg"))); // B's untouched
    }
}
