namespace Office.Api.Channels.Instagram;

/// <summary>
/// Буҷаи соатии дархостҳои follow-check — Meta ба апп то 200 дархост дар як соат иҷозат медиҳад
/// (спецификатсияи Фазаи 11). Ин синф ҳадди 80%-ро (масалан 160) татбиқ мекунад: аз он боло,
/// дархости нав ба Meta НАМЕРАВАД — InstagramProvider.CheckFollowStatusAsync Unknown бе HTTP
/// бармегардонад (fail-open — ҳамон рафтори хатои воқеӣ). Танзими 200 дар NotFollowing-ро низ
/// дар бар мегирад, чунки он акнун кэш намешавад (ниг. CheckFollowStatusAsync).
///
/// Singleton, ҳисоби дар хотир (лок-бехатар) — кофист барои як instance-и сервер (VPS-и ягона,
/// ниг. docs/deploy-runbook.md); агар дар оянда якчанд instance лозим шавад, ба ҳисоби умумӣ
/// (масалан Redis) гузаштан лозим мешавад.
/// </summary>
public class InstagramFollowCheckRateLimiter
{
    private readonly object gate = new();
    private DateTimeOffset windowStart;
    private int count;

    public bool TryConsume(int limit, DateTimeOffset now)
    {
        lock (gate)
        {
            if (now - windowStart >= TimeSpan.FromHours(1))
            {
                windowStart = now;
                count = 0;
            }

            if (count >= limit)
                return false;

            count++;
            return true;
        }
    }
}
