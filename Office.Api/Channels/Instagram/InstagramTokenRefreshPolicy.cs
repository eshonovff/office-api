namespace Office.Api.Channels.Instagram;

/// <summary>
/// Ҳам InstagramTokenRefreshJob (тасмим — оё имрӯз нав кардан кӯшиш кунад) ва ҳам тестҳояш
/// истифода мебаранд — pure, бе DB/HTTP.
/// </summary>
public static class InstagramTokenRefreshPolicy
{
    /// <summary>
    /// Meta ig_refresh_token-ро танҳо барои токене иҷозат медиҳад, ки ҳанӯз эътибор дорад ва аз
    /// сохта шуданаш ҳадди ақал 24 соат гузаштааст — токени тозаи ig_exchange_token ҳамеша хеле
    /// дуртар аз анҷом аст (~60 рӯз), пас ин шарт дар амал ҳамеша дуруст мешавад вақте нав кардан
    /// воқеан лозим аст (10 рӯз то анҷом). Танҳо ҳамчун ҳимояи охирин нигоҳ дошта мешавад.
    /// </summary>
    public static readonly TimeSpan RefreshWindow = TimeSpan.FromDays(10);

    /// <summary>True вақте эътибори токен дар доираи RefreshWindow-и оянда меафтад — нав кардан лозим аст.</summary>
    public static bool ShouldRefresh(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is not null && expiresAt.Value <= now + RefreshWindow;

    /// <summary>True вақте токен аллакай мӯҳлаташ гузаштааст — навсозии худкор дигар маъно надорад, пайвастшавии дастӣ лозим аст.</summary>
    public static bool IsPastExpiry(DateTimeOffset? expiresAt, DateTimeOffset now) =>
        expiresAt is not null && expiresAt.Value <= now;
}
