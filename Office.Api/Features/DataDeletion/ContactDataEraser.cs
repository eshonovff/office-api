using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;

namespace Office.Api.Features.DataDeletion;

/// <summary>
/// Removes one contact (a person on one channel) and everything the system kept about them: the
/// conversation with its messages, their files, tags, variables and assignment history; the
/// flow sessions they went through; their comments on this channel and the account's replies
/// under them; the comment auto-reply runs about them; the broadcasts' record of them; the goals
/// they reached in automations (conversions); and the raw
/// webhook payloads that carried their messages or comments. Nothing on Instagram itself is
/// touched. If they write again, they come back as a new contact.
///
/// As in ChannelDataEraser: rows are loaded and removed in ONE SaveChanges, the caller wraps it in
/// a transaction, and files go only after the commit (a failed commit must not have lost them).
/// Every query is by this contact's own ids — within a мизоҷ request the tenant filter narrows it
/// further, never widens it.
/// </summary>
public static class ContactDataEraser
{
    /// <returns>The contact's stored files (relative to the uploads root), to delete after the commit; null if no such contact.</returns>
    public static async Task<IReadOnlyList<string>?> EraseAsync(AppDbContext db, Guid contactId, CancellationToken ct)
    {
        var contact = await db.Conversations
            .Where(c => c.Id == contactId)
            .Select(c => new { c.Id, c.ChannelId, c.ExternalId, ChannelExternalId = c.Channel.ExternalId })
            .FirstOrDefaultAsync(ct);
        if (contact is null)
            return null;

        await DeleteRawWebhookLogsAsync(db, contact.ChannelExternalId, contact.ExternalId, ct);

        var messages = await db.Messages.Where(m => m.ConversationId == contact.Id).ToListAsync(ct);
        var channelFolder = $"whatsapp-media/{contact.ChannelId}/";
        var files = messages
            .SelectMany(m => new[] { m.MediaUrl, m.ThumbnailUrl })
            .Where(p => !string.IsNullOrEmpty(p) && p.Replace('\\', '/').StartsWith(channelFolder, StringComparison.Ordinal))
            .Select(p => p!)
            .Distinct()
            .ToList();

        var sessions = await db.FlowSessions.Where(s => s.ContactId == contact.Id).ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();
        db.FlowSessionSteps.RemoveRange(await db.FlowSessionSteps.Where(s => sessionIds.Contains(s.SessionId)).ToListAsync(ct));
        db.FlowSessions.RemoveRange(sessions);

        db.AutomationRuns.RemoveRange(await db.AutomationRuns
            .Where(r => r.Rule.ChannelId == contact.ChannelId && r.ActorExternalId == contact.ExternalId).ToListAsync(ct));

        var comments = await db.InstagramComments
            .Where(c => c.ChannelId == contact.ChannelId && c.AuthorExternalId == contact.ExternalId).ToListAsync(ct);
        var commentIds = comments.Select(c => c.ExternalId).ToList();
        db.InstagramComments.RemoveRange(comments);
        db.InstagramComments.RemoveRange(await db.InstagramComments
            .Where(c => c.ChannelId == contact.ChannelId && c.ParentExternalId != null && commentIds.Contains(c.ParentExternalId)).ToListAsync(ct));

        db.BroadcastRecipients.RemoveRange(await db.BroadcastRecipients.Where(r => r.ContactId == contact.Id).ToListAsync(ct));
        db.FlowConversions.RemoveRange(await db.FlowConversions.Where(c => c.ContactId == contact.Id).ToListAsync(ct));
        db.ContactTags.RemoveRange(await db.ContactTags.Where(t => t.ContactId == contact.Id).ToListAsync(ct));
        db.ContactVariables.RemoveRange(await db.ContactVariables.Where(v => v.ContactId == contact.Id).ToListAsync(ct));
        db.ConversationAssignmentEvents.RemoveRange(
            await db.ConversationAssignmentEvents.Where(e => e.ConversationId == contact.Id).ToListAsync(ct));
        db.Messages.RemoveRange(messages);
        db.Conversations.RemoveRange(await db.Conversations.Where(c => c.Id == contact.Id).ToListAsync(ct));

        await db.SaveChangesAsync(ct);
        return files;
    }

    /// <summary>After the commit: the contact's files, each only if it resolves inside the uploads root.</summary>
    public static void DeleteFiles(string uploadsRoot, IEnumerable<string> relativePaths)
    {
        foreach (var relativePath in relativePaths)
        {
            var path = SafeUploadsPath.TryResolve(uploadsRoot, relativePath);
            if (path is not null && File.Exists(path))
                File.Delete(path);
        }
    }

    /// <summary>
    /// Raw payloads of this channel that carried this person: a message they sent (sender), one
    /// the account sent them (echo — recipient), or a comment of theirs (changes[].value.from).
    /// A payload batching several people goes as a whole — it is debugging data, kept 30 days.
    /// PostgreSQL only (jsonb containment); the in-memory test provider has no such table.
    /// </summary>
    private static async Task DeleteRawWebhookLogsAsync(AppDbContext db, string channelExternalId, string personId, CancellationToken ct)
    {
        if (!db.Database.IsNpgsql())
            return;

        await db.Database.ExecuteSqlInterpolatedAsync($@"
            DELETE FROM webhook_logs
            WHERE raw_json @> jsonb_build_object('entry', jsonb_build_array(jsonb_build_object('id', {channelExternalId},
                      'messaging', jsonb_build_array(jsonb_build_object('sender', jsonb_build_object('id', {personId}))))))
               OR raw_json @> jsonb_build_object('entry', jsonb_build_array(jsonb_build_object('id', {channelExternalId},
                      'messaging', jsonb_build_array(jsonb_build_object('recipient', jsonb_build_object('id', {personId}))))))
               OR raw_json @> jsonb_build_object('entry', jsonb_build_array(jsonb_build_object('id', {channelExternalId},
                      'changes', jsonb_build_array(jsonb_build_object('value', jsonb_build_object('from', jsonb_build_object('id', {personId})))))))",
            ct);
    }
}
