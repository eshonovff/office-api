using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Subscriptions;

/// <summary>
/// No background job: every read or decision that depends on which requests are still open
/// first brings them up to date here — the same "computed on read" idea as customer access.
/// </summary>
internal static class SubscriptionRequestExpiry
{
    /// <summary>
    /// AwaitingPayment past its deadline (<see cref="PaymentWindow.IsPastDeadline"/>) → Expired,
    /// for all customers — an expired request must also stop holding its amount for others.
    /// </summary>
    public static Task<int> ExpireOverdueAsync(AppDbContext db, TimeSpan window, DateTimeOffset now, CancellationToken ct)
    {
        var cutoff = now - window;
        return db.SubscriptionRequests
            .Where(r => r.Status == SubscriptionRequestStatus.AwaitingPayment && r.CreatedAt < cutoff)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, SubscriptionRequestStatus.Expired), ct);
    }
}
