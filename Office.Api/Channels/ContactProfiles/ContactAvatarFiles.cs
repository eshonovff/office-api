using Office.Api.Data.Entities;
using Office.Api.Features.Conversations;

namespace Office.Api.Channels.ContactProfiles;

/// <summary>
/// Our copy of a contact's picture, as the apps see it: a link to our own endpoint (never Meta's
/// expiring one), with ?v= changing whenever the picture does; and the file itself, served
/// only after the endpoint has checked who is asking.
/// </summary>
public static class ContactAvatarFiles
{
    public static string? StaffLink(Guid conversationId, string? avatarPath) => Link("/api/conversations", conversationId, avatarPath);

    public static string? CustomerLink(Guid conversationId, string? avatarPath) => Link("/api/public/conversations", conversationId, avatarPath);

    private static string? Link(string basePath, Guid conversationId, string? avatarPath) =>
        avatarPath is null ? null : $"{basePath}/{conversationId}/avatar?v={Path.GetFileNameWithoutExtension(avatarPath)}";

    /// <summary>The picture (a JPEG we made); 404 when there is none. A link's ?v= changes with the picture, so it may be kept.</summary>
    public static IResult Serve(string? avatarPath, HttpContext http, IConfiguration configuration, IWebHostEnvironment env)
    {
        if (avatarPath is null)
            return Results.NotFound();
        http.Response.Headers.CacheControl = "private, max-age=604800";
        http.Response.Headers.XContentTypeOptions = "nosniff";
        return MessagesEndpoints.ServeStoredFileAsync(avatarPath, "image/jpeg", null, MessageType.Image, configuration, env);
    }
}
