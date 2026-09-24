using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Tests.Common;

/// <summary>
/// The staff guard must refuse мизоҷ channels on its own — even on a DbContext with NO tenant
/// filter (System), the situation of a caller that isn't a filtered HTTP request. Owner/Admin is
/// the dangerous case: "can see all channels" used to mean "yes" without asking the database.
/// </summary>
public class ChannelAccessGuardTenantTests
{
    private readonly AppDbContext _db =
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly Guid _ownerId = Guid.NewGuid();
    private readonly Guid _companyChannelId = Guid.NewGuid();
    private readonly Guid _customerChannelId = Guid.NewGuid();
    private readonly ClaimsPrincipal _owner;

    public ChannelAccessGuardTenantTests()
    {
        var ownerRole = new Role { Id = Guid.NewGuid(), Key = RoleKeys.Owner, Name = "Owner" };
        var customerId = Guid.NewGuid();
        _db.Roles.Add(ownerRole);
        _db.Users.Add(new User { Id = _ownerId, FullName = "Owner", Username = "owner", PasswordHash = "x" });
        _db.UserRoles.Add(new UserRole { UserId = _ownerId, RoleId = ownerRole.Id });
        _db.Customers.Add(new Customer { Id = customerId, Email = "m@example.com", FullName = "M" });
        _db.Channels.AddRange(
            new Channel { Id = _companyChannelId, Type = ChannelType.Instagram, Name = "company", ExternalId = "c" },
            new Channel { Id = _customerChannelId, Type = ChannelType.Instagram, Name = "mizoj", ExternalId = "m", CustomerId = customerId });
        _db.Conversations.AddRange(
            new Conversation { Id = Guid.NewGuid(), ChannelId = _companyChannelId, ExternalId = "c1" },
            new Conversation { Id = Guid.NewGuid(), ChannelId = _customerChannelId, ExternalId = "m1" });
        _db.SaveChanges();

        _owner = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(JwtRegisteredClaimNames.Sub, _ownerId.ToString()), new Claim(ClaimTypes.Role, RoleKeys.Owner)],
            AuthSchemes.StaffIdentity));
    }

    [Fact]
    public async Task HasAccess_OwnerIsRefusedAMizojChannel()
    {
        // InboxHub.JoinChannel's check — the live message stream of that channel.
        var guard = new ChannelAccessGuard(_db);

        Assert.True(await guard.HasAccessAsync(_owner, _companyChannelId, assignedTo: null, CancellationToken.None));
        Assert.False(await guard.HasAccessAsync(_owner, _customerChannelId, assignedTo: null, CancellationToken.None));
    }

    [Fact]
    public async Task CanAccessChannel_OwnerIsRefusedAMizojChannel()
    {
        var guard = new ChannelAccessGuard(_db);

        Assert.True(await guard.CanAccessChannelAsync(_owner, _companyChannelId, CancellationToken.None));
        Assert.False(await guard.CanAccessChannelAsync(_owner, _customerChannelId, CancellationToken.None));
    }

    [Fact]
    public async Task ChannelList_LeavesOutMizojChannels()
    {
        var (query, _) = await new ChannelAccessGuard(_db).ApplyChannelAccessFilterAsync(_db.Channels, _owner, CancellationToken.None);

        Assert.Equal([_companyChannelId], await query.Select(c => c.Id).ToListAsync());
    }

    [Fact]
    public async Task ConversationList_LeavesOutMizojConversations()
    {
        var query = await new ChannelAccessGuard(_db).ApplyAccessFilterAsync(_db.Conversations, _owner, CancellationToken.None);

        Assert.Equal(["c1"], await query.Select(c => c.ExternalId).ToListAsync());
    }

    [Fact]
    public async Task AssignableUsers_NobodyForAMizojChannel()
    {
        var guard = new ChannelAccessGuard(_db);

        Assert.True(await guard.CanUserBeAssignedToChannelAsync(_ownerId, _companyChannelId, CancellationToken.None));
        Assert.False(await guard.CanUserBeAssignedToChannelAsync(_ownerId, _customerChannelId, CancellationToken.None));
        Assert.Empty(await guard.ApplyAssignableUsersFilter(_db.Users, _customerChannelId).ToListAsync());
    }
}
