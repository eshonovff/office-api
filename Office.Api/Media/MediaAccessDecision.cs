namespace Office.Api.Media;

public enum MediaAccessOutcome
{
    NotFound,
    DownloadFailed,
    Deleted,
    Ready,
}

/// <summary>
/// Кадом ҷавоб барои `GET /api/messages/{id}/media|thumbnail` — pure, бе DB/файл.
/// `hasAccess` аллакай натиҷаи `IChannelAccessGuard` аст (405/403 ҳамеша 404 барои
/// пинҳон кардани мавҷудият, тибқи алгуи мавҷудаи Conversations).
/// </summary>
public static class MediaAccessDecision
{
    public static MediaAccessOutcome Evaluate(
        bool messageFound, bool hasAccess, string? storedRelativePath, string? downloadError, DateTimeOffset? deletedAt)
    {
        if (!messageFound || !hasAccess)
            return MediaAccessOutcome.NotFound;

        if (downloadError is not null)
            return MediaAccessOutcome.DownloadFailed;

        if (deletedAt is not null)
            return MediaAccessOutcome.Deleted;

        return storedRelativePath is null ? MediaAccessOutcome.NotFound : MediaAccessOutcome.Ready;
    }
}
