using System.Text.Json;

namespace Office.Api.Channels.Instagram;

/// <summary>A comment as the Graph API returns it (GET /{media-id}/comments), replies included.</summary>
public record InstagramCommentItem(
    string Id,
    string? ParentId,
    string AuthorId,
    string? AuthorUsername,
    string Text,
    DateTimeOffset? Timestamp,
    bool Hidden);

/// <summary>Pure parsing of the comment endpoints' JSON — kept apart from the HTTP so it is tested alone.</summary>
public static class InstagramCommentParser
{
    public static string? TryReadId(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>One page: every comment and each one's replies (flattened, ParentId set), plus the next cursor.</summary>
    public static (List<InstagramCommentItem> Items, string? NextCursor) ParsePage(string body)
    {
        var items = new List<InstagramCommentItem>();
        using var doc = JsonDocument.Parse(body);

        if (doc.RootElement.TryGetProperty("data", out var dataEl) && dataEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var comment in dataEl.EnumerateArray())
            {
                var parsed = ParseOne(comment, parentId: null);
                if (parsed is null)
                    continue;
                items.Add(parsed);

                if (comment.TryGetProperty("replies", out var repliesEl) &&
                    repliesEl.TryGetProperty("data", out var replyData) && replyData.ValueKind == JsonValueKind.Array)
                {
                    foreach (var reply in replyData.EnumerateArray())
                    {
                        if (ParseOne(reply, parsed.Id) is { } r)
                            items.Add(r);
                    }
                }
            }
        }

        var next = doc.RootElement.TryGetProperty("paging", out var pagingEl) &&
            pagingEl.TryGetProperty("next", out _) &&
            pagingEl.TryGetProperty("cursors", out var cursorsEl) &&
            cursorsEl.TryGetProperty("after", out var afterEl)
            ? afterEl.GetString()
            : null;

        return (items, next);
    }

    private static InstagramCommentItem? ParseOne(JsonElement el, string? parentId)
    {
        var id = el.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        // "from" carries the author's id; very old comments may only have "username".
        var authorId = el.TryGetProperty("from", out var fromEl) && fromEl.TryGetProperty("id", out var fromId) ? fromId.GetString() : null;
        var username = el.TryGetProperty("username", out var userEl) ? userEl.GetString()
            : fromEl.ValueKind == JsonValueKind.Object && fromEl.TryGetProperty("username", out var fromUser) ? fromUser.GetString() : null;
        if (string.IsNullOrEmpty(id) || (string.IsNullOrEmpty(authorId) && string.IsNullOrEmpty(username)))
            return null;

        DateTimeOffset? timestamp = el.TryGetProperty("timestamp", out var tsEl) && DateTimeOffset.TryParse(tsEl.GetString(), out var ts)
            ? ts
            : null;

        return new InstagramCommentItem(
            id,
            parentId,
            authorId ?? $"username:{username}",
            username,
            el.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "",
            timestamp,
            el.TryGetProperty("hidden", out var hiddenEl) && hiddenEl.ValueKind == JsonValueKind.True);
    }
}
