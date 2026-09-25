using Office.Api.Data.Entities;

namespace Office.Api.Features.Subscriptions;

public enum CustomerAccessStatus
{
    Trial,
    Active,
    Expired,
}

public readonly record struct CustomerAccess(CustomerAccessStatus Status, CustomerPlanTier? Tier, DateTimeOffset? EndsAt)
{
    public bool HasAccess => Status != CustomerAccessStatus.Expired;
}

public static class CustomerAccessResolver
{
    public static CustomerAccess Resolve(
        DateTimeOffset now,
        DateTimeOffset? trialEndsAt,
        CustomerPlanTier? planTier,
        DateTimeOffset? planExpiresAt)
    {
        // A paid plan wins over a still-running trial: paying during the trial must not leave
        // the customer looking like a trial user.
        if (planTier is not null && planExpiresAt is not null && now < planExpiresAt.Value)
            return new CustomerAccess(CustomerAccessStatus.Active, planTier, planExpiresAt);

        if (trialEndsAt is not null && now < trialEndsAt.Value)
            return new CustomerAccess(CustomerAccessStatus.Trial, null, trialEndsAt);

        // Keep the last tier/date so the UI can say "your Pro plan ended on …".
        return new CustomerAccess(CustomerAccessStatus.Expired, planTier, planExpiresAt ?? trialEndsAt);
    }
}
