using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Email;

namespace Office.Api.Features.CustomerAuth;

/// <summary>
/// Creates a reset link for an email and sends it — only to a verified, active account, at
/// most once a minute and five times a day (PasswordResetRules). The plain token lives only
/// here and in the email; the database gets its hash. A new link voids the earlier ones.
/// </summary>
public class PasswordResetSender(
    AppDbContext db,
    IEmailSender emailSender,
    IConfiguration configuration,
    ILogger<PasswordResetSender> logger)
{
    public async Task<PasswordResetSendResult> SendAsync(string normalizedEmail, DateTimeOffset now, CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == normalizedEmail, ct);
        // An unverified account never proved it owns the email; a switched-off one may not sign in.
        if (customer is null || !customer.IsActive || customer.EmailVerifiedAt is null)
            return PasswordResetSendResult.NoAccount;

        var windowStart = now - PasswordResetRules.CapWindow;
        var recent = await db.CustomerPasswordResets
            .Where(r => r.CustomerId == customer.Id && r.CreatedAt > windowStart)
            .ToListAsync(ct);

        var decision = PasswordResetRules.CanSend(recent.Select(r => r.CreatedAt).ToList(), now);
        if (decision != PasswordResetSendResult.Sent)
        {
            logger.LogWarning("Password reset for customer {CustomerId} not sent: {Reason}", customer.Id, decision);
            return decision;
        }

        foreach (var earlier in recent.Where(r => r.ConsumedAt is null))
            earlier.ConsumedAt = now; // only the newest link works

        // This account's rows from before the window no longer count for anything.
        db.CustomerPasswordResets.RemoveRange(
            await db.CustomerPasswordResets.Where(r => r.CustomerId == customer.Id && r.CreatedAt <= windowStart).ToListAsync(ct));

        var (token, hash) = PasswordResetRules.NewToken();
        db.CustomerPasswordResets.Add(new CustomerPasswordReset
        {
            Id = Guid.CreateVersion7(),
            CustomerId = customer.Id,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = now.Add(PasswordResetRules.LinkLifetime),
        });
        await db.SaveChangesAsync(ct);

        // The address comes from configuration, never from the request (a forged Host header
        // must not be able to point the link at someone else's site). The token goes in the
        // fragment, which browsers never send to a server or in a Referer.
        var link = $"{AppUrls.GetPublicUrl(configuration)}/reset-password#token={token}";
        await emailSender.SendAsync(
            customer.Email,
            "Барқарор кардани рамз — office.nizom.tj",
            $"Салом, {customer.FullName}!\n\nБарои гузоштани рамзи нав ин пайвандро кушоед:\n{link}\n\n" +
            "Пайванд 30 дақиқа ва танҳо як бор амал мекунад.\n\n" +
            "Агар шумо ин дархостро накарда бошед, ин email-ро нодида гиред — рамзи шумо иваз намешавад.",
            ct);

        logger.LogInformation("Password reset link sent to customer {CustomerId}", customer.Id);
        return PasswordResetSendResult.Sent;
    }
}
