using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Realtime;

namespace Office.Api.Channels.Instagram;

/// <summary>
/// ig_exchange_token дар InstagramOAuthConnector токени дарозмуддат (~60 рӯз) медиҳад, вале худи
/// он ҳам мӯҳлат дорад — бе ин job, ҳар канали Instagram рӯзе бидуни огоҳӣ пайвастшавиро аз даст
/// медод (ниг. report). Ҳар рӯз каналҳои ба анҷом наздикро (InstagramTokenRefreshPolicy) бо
/// ig_refresh_token худкор нав мекунад; агар нашавад, Owner-ро огоҳ мекунад ва, агар аллакай
/// гузашта бошад, RequiresReconnect-ро мегузорад (пайвастшавии дастӣ лозим).
/// </summary>
[Queue("media-maintenance")]
public class InstagramTokenRefreshJob(
    AppDbContext db,
    HttpClient httpClient,
    IChannelCredentialsProtector protector,
    INotificationService notificationService,
    ILogger<InstagramTokenRefreshJob> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;

        // Шумораи каналҳо дар ин система хурд аст — филтр дар C# (на дар SQL), то ҳамон
        // InstagramTokenRefreshPolicy pure-ро истифода барем, на такрори мантиқ дар LINQ-и EF.
        var channels = await db.Channels
            .Where(c => c.Type == ChannelType.Instagram && c.IsActive && c.CredentialsExpiresAt != null)
            .ToListAsync(ct);

        foreach (var channel in channels)
        {
            if (!InstagramTokenRefreshPolicy.ShouldRefresh(channel.CredentialsExpiresAt, now))
                continue;

            try
            {
                await RefreshOneAsync(channel, ct);
                logger.LogInformation(
                    "InstagramTokenRefreshJob: токени канали {ChannelId} нав шуд, анҷоми нав={ExpiresAt}",
                    channel.Id, channel.CredentialsExpiresAt);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "InstagramTokenRefreshJob: нав кардани токен барои канали {ChannelId} ноком шуд", channel.Id);

                // Мӯҳлат ҳанӯз нагузаштааст — ҷои умед боқист (job фардо боз кӯшиш мекунад), пас
                // танҳо огоҳӣ, на RequiresReconnect (то бо флеп бе сабаб корбарро гумроҳ накунад).
                var pastExpiry = InstagramTokenRefreshPolicy.IsPastExpiry(channel.CredentialsExpiresAt, now);
                if (pastExpiry)
                    channel.RequiresReconnect = true;

                await NotifyOwnersAsync(
                    channel,
                    pastExpiry
                        ? $"Instagram: токени канали «{channel.Name}» аллакай эътибор надорад. Дубора пайваст кунед."
                        : $"Instagram: токени канали «{channel.Name}» ба зудӣ анҷом меёбад ва навсозии худкор ноком шуд. Дубора пайваст кунед.",
                    ct);
            }
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task RefreshOneAsync(Channel channel, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(channel.CredentialsEncrypted))
            throw new InvalidOperationException("Канал credentials надорад.");

        var credentials = InstagramCredentials.Parse(protector.Unprotect(channel.CredentialsEncrypted));

        // ig_refresh_token — endpoint-и ҷудогона аз ig_exchange_token (InstagramOAuthConnector):
        // танҳо токени АЛЛАКАЙ дарозмуддатро нав мекунад, на code-ро ба token табдил медиҳад.
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://graph.instagram.com/refresh_access_token?grant_type=ig_refresh_token" +
            $"&access_token={Uri.EscapeDataString(credentials.AccessToken)}");
        var response = await httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(ct));
        var newToken = doc.RootElement.GetProperty("access_token").GetString()!;
        var expiresInSeconds = doc.RootElement.GetProperty("expires_in").GetInt64();

        channel.CredentialsEncrypted = protector.Protect(JsonSerializer.Serialize(credentials with { AccessToken = newToken }));
        channel.CredentialsExpiresAt = DateTimeOffset.UtcNow.AddSeconds(expiresInSeconds);
        channel.RequiresReconnect = false;
    }

    private async Task NotifyOwnersAsync(Channel channel, string message, CancellationToken ct)
    {
        var ownerIds = await db.Users
            .Where(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner))
            .Select(u => u.Id)
            .ToListAsync(ct);

        foreach (var ownerId in ownerIds)
            await notificationService.PushAsync(ownerId, "instagram_token_expiring", new { message, channelId = channel.Id }, ct);
    }
}
