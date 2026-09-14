namespace Office.Api.Channels.Automation;

/// <summary>
/// Гардиши ҷавобҳо: round-robin, на тасодуфӣ — детерминистӣ (санҷиданаш осон, такрористеҳсол
/// мешавад) ва аз матни якхела дар пай ҳам худдорӣ мекунад, ки Instagram-ро аз шубҳаи спам дур
/// мекунад. Индекс аз рӯи шумораи run-ҳои қаблии ҳамин rule ҳисоб карда мешавад — на аз рӯи
/// вақти job (то race бо якчанд комментарии ҳамзамон индексро дучанд напартояд).
/// </summary>
public static class CommentReplySelector
{
    public static string Select(IReadOnlyList<string> replies, int priorRunCount) =>
        replies[priorRunCount % replies.Count];
}
