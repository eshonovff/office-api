using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Common;

public interface IChannelAccessGuard
{
    /// <summary>Барои GET /api/conversations — филтри рӯйхат, як ҷои умумӣ (6.14), на такрор дар ҳар endpoint.</summary>
    Task<IQueryable<Conversation>> ApplyAccessFilterAsync(IQueryable<Conversation> query, ClaimsPrincipal principal, CancellationToken ct);

    /// <summary>Барои GET/{id}, .../messages, PATCH — санҷиши як conversation-и мушаххас.</summary>
    Task<bool> HasAccessAsync(ClaimsPrincipal principal, Guid channelId, Guid? assignedTo, CancellationToken ct);

    /// <summary>
    /// Барои GET /api/channels/mine — кадом каналҳо (на conversation-и мушаххас) корбар
    /// мебинад, ва оё онҳо barои SignalR JoinChannel joinable ҳастанд.
    /// </summary>
    Task<(IQueryable<Channel> Query, bool Joinable)> ApplyChannelAccessFilterAsync(
        IQueryable<Channel> query, ClaimsPrincipal principal, CancellationToken ct);

    /// <summary>Барои GET /{id}, /{id}/whatsapp-templates — санҷиши як канали мушаххас (на рӯйхат).</summary>
    Task<bool> CanAccessChannelAsync(ClaimsPrincipal principal, Guid channelId, CancellationToken ct);

    /// <summary>
    /// Барои PATCH /conversations/{id} бо assignedTo — оё корбари ҳадаф (на principal-и
    /// дархосткунанда) баъд аз таъин ин чатро мебинад. Owner/Admin ҳамеша ҳа; дигарон бояд
    /// узви канали ин чат бошанд — вагарна таъиноти "орфан" мешавад (ниг. bug: assignment
    /// bypasses channel access). Ҳамон маҷмӯъ бо ApplyAssignableUsersFilter (як ҷои ягона).
    /// </summary>
    Task<bool> CanUserBeAssignedToChannelAsync(Guid userId, Guid channelId, CancellationToken ct);

    /// <summary>
    /// Барои GET /channels/{id}/assignable-users, /conversations/{id}/assignable-users —
    /// маҷмӯи пурраи корбароне, ки ба ин канал таъиншаванда ҳастанд (узв + Owner/Admin).
    /// Ҳамон формула бо CanUserBeAssignedToChannelAsync — то рӯйхат ва санҷиш ҳеҷ гоҳ дур нашаванд.
    /// </summary>
    IQueryable<User> ApplyAssignableUsersFilter(IQueryable<User> query, Guid channelId);
}

public class ChannelAccessGuard(AppDbContext db) : IChannelAccessGuard
{
    public static bool CanSeeAllChannels(ClaimsPrincipal principal) =>
        principal.IsInRole(RoleKeys.Owner) || principal.IsInRole(RoleKeys.Admin);

    public async Task<IQueryable<Conversation>> ApplyAccessFilterAsync(
        IQueryable<Conversation> query, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();

        if (!CanSeeAllChannels(principal))
            query = query.Where(c => c.Channel.Members.Any(m => m.UserId == userId));

        if (await GetOnlyAssignedAsync(userId, ct))
            query = query.Where(c => c.AssignedTo == userId);

        return query;
    }

    public async Task<bool> HasAccessAsync(ClaimsPrincipal principal, Guid channelId, Guid? assignedTo, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var canSeeAll = CanSeeAllChannels(principal);

        var isMember = canSeeAll || await db.ChannelMembers.AnyAsync(m => m.ChannelId == channelId && m.UserId == userId, ct);
        var onlyAssigned = await GetOnlyAssignedAsync(userId, ct);

        return ConversationAccessResolver.CanAccess(canSeeAllChannels: false, isMember, onlyAssigned, assignedTo == userId);
    }

    public async Task<(IQueryable<Channel> Query, bool Joinable)> ApplyChannelAccessFilterAsync(
        IQueryable<Channel> query, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var onlyAssigned = await GetOnlyAssignedAsync(userId, ct);
        var policy = ChannelListAccessResolver.Resolve(CanSeeAllChannels(principal), onlyAssigned);

        var filtered = policy.Scope switch
        {
            ChannelListScope.All => query,
            ChannelListScope.MembersOnly => query.Where(c => c.Members.Any(m => m.UserId == userId)),
            ChannelListScope.AssignedOnly => query.Where(c => c.Conversations.Any(conv => conv.AssignedTo == userId)),
            _ => query.Where(c => false),
        };

        return (filtered, policy.Joinable);
    }

    public async Task<bool> CanAccessChannelAsync(ClaimsPrincipal principal, Guid channelId, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var onlyAssigned = await GetOnlyAssignedAsync(userId, ct);
        var policy = ChannelListAccessResolver.Resolve(CanSeeAllChannels(principal), onlyAssigned);

        var isInScope = policy.Scope switch
        {
            ChannelListScope.MembersOnly =>
                await db.ChannelMembers.AnyAsync(m => m.ChannelId == channelId && m.UserId == userId, ct),
            ChannelListScope.AssignedOnly =>
                await db.Conversations.AnyAsync(c => c.ChannelId == channelId && c.AssignedTo == userId, ct),
            _ => false,
        };

        return ChannelListAccessResolver.CanAccessChannel(policy.Scope, isInScope);
    }

    public Task<bool> CanUserBeAssignedToChannelAsync(Guid userId, Guid channelId, CancellationToken ct) =>
        ApplyAssignableUsersFilter(db.Users.AsNoTracking(), channelId).AnyAsync(u => u.Id == userId, ct);

    public IQueryable<User> ApplyAssignableUsersFilter(IQueryable<User> query, Guid channelId) =>
        query.Where(u =>
            u.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner || ur.Role.Key == RoleKeys.Admin) ||
            db.ChannelMembers.Any(m => m.ChannelId == channelId && m.UserId == u.Id));

    private Task<bool> GetOnlyAssignedAsync(Guid userId, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.OnlyAssigned).FirstAsync(ct);
}
