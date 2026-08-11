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

    /// <summary>Барои GET /api/channels/mine — кадом каналҳо (на conversation-и мушаххас) корбар мебинад.</summary>
    Task<IQueryable<Channel>> ApplyChannelAccessFilterAsync(IQueryable<Channel> query, ClaimsPrincipal principal, CancellationToken ct);
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

    public async Task<IQueryable<Channel>> ApplyChannelAccessFilterAsync(
        IQueryable<Channel> query, ClaimsPrincipal principal, CancellationToken ct)
    {
        var userId = principal.GetUserId();
        var onlyAssigned = await GetOnlyAssignedAsync(userId, ct);
        var scope = ChannelListAccessResolver.Resolve(CanSeeAllChannels(principal), onlyAssigned);

        return scope switch
        {
            ChannelListScope.All => query,
            ChannelListScope.MembersOnly => query.Where(c => c.Members.Any(m => m.UserId == userId)),
            _ => query.Where(c => false),
        };
    }

    private Task<bool> GetOnlyAssignedAsync(Guid userId, CancellationToken ct) =>
        db.Users.Where(u => u.Id == userId).Select(u => u.OnlyAssigned).FirstAsync(ct);
}
