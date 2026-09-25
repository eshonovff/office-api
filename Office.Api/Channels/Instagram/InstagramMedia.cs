namespace Office.Api.Channels.Instagram;

/// <summary>Як пост аз GET /{ig-user-id}/media — барои интихоби пост дар UI-и автоматизатсия.</summary>
public record InstagramMediaItem(string Id, string? MediaType, string? ImageUrl, string? Permalink, string? Caption, string? Timestamp);

public record InstagramMediaPage(IReadOnlyList<InstagramMediaItem> Items, string? NextCursor);
