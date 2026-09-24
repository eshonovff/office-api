using Microsoft.EntityFrameworkCore;
using Office.Api.Common;
using Office.Api.Data;

namespace Office.Api.Features.DataDeletion;

/// <summary>
/// Removes channels and everything under them: contacts (conversations) with their messages,
/// tags, variables and assignment history; flows with nodes, edges, sessions and steps;
/// comment-automation rules and runs; members; the channel itself. Used by Meta's data-deletion
/// callback (DataDeletionJob) and by a мизоҷ deleting their account.
///
/// Rows are loaded and removed in ONE SaveChanges — EF orders the deletes by foreign key, and
/// the caller's transaction makes it all-or-nothing. (ExecuteDelete would be faster, but the
/// InMemory test provider can't run it, and deletion is exactly the code that must be tested.)
/// Every query is by explicit channel id — within a мизоҷ request the tenant filter narrows it
/// further, never widens it.
/// </summary>
public static class ChannelDataEraser
{
    public static async Task EraseAsync(AppDbContext db, IReadOnlyCollection<Guid> channelIds, CancellationToken ct)
    {
        if (channelIds.Count == 0)
            return;

        var conversationIds = await db.Conversations.Where(c => channelIds.Contains(c.ChannelId)).Select(c => c.Id).ToListAsync(ct);
        var flowIds = await db.Flows.Where(f => channelIds.Contains(f.ChannelId)).Select(f => f.Id).ToListAsync(ct);
        var ruleIds = await db.AutomationRules.Where(r => channelIds.Contains(r.ChannelId)).Select(r => r.Id).ToListAsync(ct);

        // Sessions hang off both a flow and a contact — either side is reason enough to go.
        var sessions = await db.FlowSessions
            .Where(s => flowIds.Contains(s.FlowId) || conversationIds.Contains(s.ContactId)).ToListAsync(ct);
        var sessionIds = sessions.Select(s => s.Id).ToList();

        db.FlowSessionSteps.RemoveRange(await db.FlowSessionSteps.Where(s => sessionIds.Contains(s.SessionId)).ToListAsync(ct));
        db.FlowSessions.RemoveRange(sessions);
        db.FlowEdges.RemoveRange(await db.FlowEdges.Where(e => flowIds.Contains(e.FlowId)).ToListAsync(ct));
        db.FlowNodes.RemoveRange(await db.FlowNodes.Where(n => flowIds.Contains(n.FlowId)).ToListAsync(ct));
        db.Flows.RemoveRange(await db.Flows.Where(f => flowIds.Contains(f.Id)).ToListAsync(ct));

        db.AutomationRuns.RemoveRange(await db.AutomationRuns.Where(r => ruleIds.Contains(r.RuleId)).ToListAsync(ct));
        db.AutomationRules.RemoveRange(await db.AutomationRules.Where(r => ruleIds.Contains(r.Id)).ToListAsync(ct));

        db.ContactTags.RemoveRange(await db.ContactTags.Where(t => conversationIds.Contains(t.ContactId)).ToListAsync(ct));
        db.ContactVariables.RemoveRange(await db.ContactVariables.Where(v => conversationIds.Contains(v.ContactId)).ToListAsync(ct));
        db.ConversationAssignmentEvents.RemoveRange(
            await db.ConversationAssignmentEvents.Where(e => conversationIds.Contains(e.ConversationId)).ToListAsync(ct));
        db.Messages.RemoveRange(await db.Messages.Where(m => conversationIds.Contains(m.ConversationId)).ToListAsync(ct));
        db.Conversations.RemoveRange(await db.Conversations.Where(c => conversationIds.Contains(c.Id)).ToListAsync(ct));

        db.ChannelMembers.RemoveRange(await db.ChannelMembers.Where(m => channelIds.Contains(m.ChannelId)).ToListAsync(ct));
        db.Channels.RemoveRange(await db.Channels.Where(c => channelIds.Contains(c.Id)).ToListAsync(ct));

        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Media of these channels (inbound and outbound share uploads/whatsapp-media/{channelId}).
    /// Call only after the transaction committed: a failed commit must not have lost files.
    /// </summary>
    public static void DeleteMediaFolders(string uploadsRoot, IEnumerable<Guid> channelIds)
    {
        foreach (var channelId in channelIds)
        {
            var folder = SafeUploadsPath.TryResolve(uploadsRoot, Path.Combine("whatsapp-media", channelId.ToString()));
            if (folder is not null && Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);
        }
    }
}
