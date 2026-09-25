using Microsoft.EntityFrameworkCore;
using Office.Api.Channels;
using Office.Api.Channels.Facebook;
using Office.Api.Channels.Instagram;
using Office.Api.Channels.Meta;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Channels;

public enum ChannelConnectOutcome
{
    Created,
    Updated,

    /// <summary>The account is already connected by a different owner — nothing was changed.</summary>
    OwnedByAnother,
}

public sealed record ChannelConnectResult(ChannelConnectOutcome Outcome, Channel? Channel);

/// <summary>
/// Saves the account picked from an OAuth session as a channel of <c>ownerCustomerId</c>
/// (null = company). Shared by the staff and the мизоҷ connect endpoints, so both follow the
/// same rule: one account, one owner.
/// </summary>
public static class OAuthChannelConnection
{
    public static async Task<ChannelConnectResult> SaveAsync(
        ChannelType type,
        ConnectableAccount account,
        string name,
        Guid? ownerCustomerId,
        AppDbContext db,
        IChannelCredentialsProtector protector,
        FacebookOAuthConnector facebookConnector,
        InstagramOAuthConnector instagramConnector,
        CancellationToken ct)
    {
        // IgnoreQueryFilters, deliberately: the caller's tenant filter hides accounts connected by
        // other owners, but (Type, ExternalId) is unique across ALL owners. Without this, connecting
        // an account a мизоҷ already has would "not find" it and then fail the insert with a 500 —
        // and a match here must never be re-owned, only refused.
        var existing = await db.Channels
            .IgnoreQueryFilters()
            .Include(c => c.Members).ThenInclude(m => m.User)
            .FirstOrDefaultAsync(c => c.Type == type && c.ExternalId == account.ExternalId, ct);

        if (existing is not null && existing.CustomerId != ownerCustomerId)
            return new ChannelConnectResult(ChannelConnectOutcome.OwnedByAnother, null);

        var credentialsEncrypted = protector.Protect(account.CredentialsJson);

        Channel channel;
        if (existing is null)
        {
            channel = new Channel
            {
                Id = Guid.CreateVersion7(),
                Type = type,
                Name = name,
                ExternalId = account.ExternalId,
                CredentialsEncrypted = credentialsEncrypted,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
                CredentialsExpiresAt = account.CredentialsExpiresAt,
                CustomerId = ownerCustomerId,
                MetaAppScopedUserId = account.AppScopedUserId,
            };
            db.Channels.Add(channel);
        }
        else
        {
            existing.Name = name;
            existing.CredentialsEncrypted = credentialsEncrypted;
            existing.IsActive = true;
            existing.CredentialsExpiresAt = account.CredentialsExpiresAt;
            existing.MetaAppScopedUserId = account.AppScopedUserId ?? existing.MetaAppScopedUserId;
            // Пайвастшавии нав (дастӣ ё худкор) ҳамеша аломати "пайвастшавӣ лозим"-ро тоза мекунад —
            // ин маҳз он чизест, ки корбар ҳоло анҷом дод.
            existing.RequiresReconnect = false;
            channel = existing;
        }

        await db.SaveChangesAsync(ct);

        // Мизоҷ ба App Dashboard дастрасӣ надорад — агар обуна нашавад, набояд хомӯш монад: канал
        // боз ҳам сохта/пайваст мешавад (SaveChangesAsync боло аллакай захира кард), вале бо сабаби
        // мушаххас қайд мешавад, то дар UI намоён бошад (ниг. Channel.WebhookSetupWarning). 2026-08-25:
        // маҳз ҳамин хомӯшӣ буд, ки "Facebook паём намерасад"-ро рӯзҳо пинҳон нигоҳ дошт.
        if (type == ChannelType.Facebook)
        {
            var facebookCredentials = FacebookCredentials.Parse(account.CredentialsJson);
            channel.WebhookSetupWarning = await facebookConnector.EnsureWebhookSubscriptionAsync(
                facebookCredentials.PageId, facebookCredentials.PageAccessToken, ct);
            await db.SaveChangesAsync(ct);
        }
        else if (type == ChannelType.Instagram)
        {
            var instagramCredentials = InstagramCredentials.Parse(account.CredentialsJson);
            channel.WebhookSetupWarning = await instagramConnector.EnsureWebhookSubscriptionAsync(
                instagramCredentials.InstagramAccountId, instagramCredentials.AccessToken, ct);
            await db.SaveChangesAsync(ct);
        }

        return new ChannelConnectResult(
            existing is null ? ChannelConnectOutcome.Created : ChannelConnectOutcome.Updated, channel);
    }

    /// <summary>Same wording for staff and мизоҷ — never says who owns the account.</summary>
    public static IResult OwnedByAnotherProblem() => Results.Problem(
        title: "Аккаунт аллакай пайваст аст",
        detail: "Ин аккаунт аллакай дар системаи дигар пайваст шудааст ва ба ин ҷо пайваст карда намешавад.",
        statusCode: StatusCodes.Status409Conflict,
        extensions: new Dictionary<string, object?> { ["code"] = "account_taken" });
}
