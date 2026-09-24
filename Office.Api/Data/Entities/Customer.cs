namespace Office.Api.Data.Entities;

/// <summary>
/// Ҳисоби мизоҷи беруна (сабти худӣ — email+parol, баъдтар Google/Apple) — комилан ҷудо аз
/// User (кормандони дохилӣ, admin-provisioned). Ин ҷудоӣ қасдӣ аст: Customer ҳеҷ гоҳ роль ё
/// permission надорад, пас хатои конфигуратсия наметавонад мизоҷро ба системаи дохилӣ бирасонад.
/// </summary>
public class Customer
{
    public Guid Id { get; set; }

    public required string Email { get; set; }

    /// <summary>Null барои ҳисоби танҳо-OAuth (Google/Apple) — email+parol ихтиёрист.</summary>
    public string? PasswordHash { get; set; }

    public required string FullName { get; set; }
    public string? AvatarUrl { get; set; }

    public DateTimeOffset? EmailVerifiedAt { get; set; }
    public string? EmailVerificationCodeHash { get; set; }
    public DateTimeOffset? EmailVerificationCodeExpiresAt { get; set; }
    public int EmailVerificationAttempts { get; set; }
    public DateTimeOffset? EmailVerificationSentAt { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }

    // Effective access (trial / active / expired) is never stored — derived from these three
    // plus "now" by CustomerAccessResolver, so an expiring plan needs no background job.
    public DateTimeOffset? TrialEndsAt { get; set; }
    public CustomerPlanTier? PlanTier { get; set; }
    public DateTimeOffset? PlanExpiresAt { get; set; }

    public ICollection<CustomerRefreshToken> RefreshTokens { get; set; } = [];
    public ICollection<CustomerExternalLogin> ExternalLogins { get; set; } = [];
}
