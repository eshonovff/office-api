using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Subscriptions;

namespace Office.Api.Channels.Flows;

/// <summary>
/// Whether automations may run on a channel right now. Company channels: always (unchanged).
/// A мизоҷ's channel: only while it is connected and the мизоҷ has access (trial or paid plan) —
/// an unpaid plan stops the automations, while the flows themselves are kept, ready for the
/// moment they pay.
/// </summary>
public static class AutomationRunGate
{
    public static bool CanRun(bool isCompanyChannel, bool channelIsActive, CustomerAccess? ownerAccess) =>
        isCompanyChannel || (channelIsActive && ownerAccess?.HasAccess == true);

    public static async Task<bool> CanRunAsync(Guid channelId, AppDbContext db, CancellationToken ct)
    {
        var channel = await db.Channels
            .Where(c => c.Id == channelId)
            .Select(c => new
            {
                c.IsActive,
                IsCompany = c.CustomerId == null,
                TrialEndsAt = c.Customer != null ? c.Customer.TrialEndsAt : null,
                PlanTier = c.Customer != null ? c.Customer.PlanTier : null,
                PlanExpiresAt = c.Customer != null ? c.Customer.PlanExpiresAt : null,
            })
            .FirstOrDefaultAsync(ct);
        if (channel is null)
            return false;

        var access = channel.IsCompany
            ? (CustomerAccess?)null
            : CustomerAccessResolver.Resolve(DateTimeOffset.UtcNow, channel.TrialEndsAt, channel.PlanTier, channel.PlanExpiresAt);
        return CanRun(channel.IsCompany, channel.IsActive, access);
    }
}
