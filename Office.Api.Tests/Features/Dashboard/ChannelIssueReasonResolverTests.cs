using Office.Api.Features.Dashboard;

namespace Office.Api.Tests.Features.Dashboard;

public class ChannelIssueReasonResolverTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan ExpiringSoonThreshold = TimeSpan.FromDays(10);

    [Fact]
    public void Resolve_NoIssues_ReturnsNull()
    {
        var reason = ChannelIssueReasonResolver.Resolve(
            requiresReconnect: false, webhookSetupWarning: null, credentialsExpiresAt: null, Now, ExpiringSoonThreshold);

        Assert.Null(reason);
    }

    [Fact]
    public void Resolve_RequiresReconnect_MentionsReconnect()
    {
        var reason = ChannelIssueReasonResolver.Resolve(
            requiresReconnect: true, webhookSetupWarning: null, credentialsExpiresAt: null, Now, ExpiringSoonThreshold);

        Assert.Equal("Пайвастшавӣ лозим аст.", reason);
    }

    [Fact]
    public void Resolve_WebhookSetupWarning_UsesItVerbatim()
    {
        var reason = ChannelIssueReasonResolver.Resolve(
            requiresReconnect: false, webhookSetupWarning: "message_echoes фаъол нест.", credentialsExpiresAt: null, Now, ExpiringSoonThreshold);

        Assert.Equal("message_echoes фаъол нест.", reason);
    }

    [Fact]
    public void Resolve_CredentialsExpiringWithinThreshold_MentionsExpiry()
    {
        var reason = ChannelIssueReasonResolver.Resolve(
            requiresReconnect: false, webhookSetupWarning: null,
            credentialsExpiresAt: Now.AddDays(5), Now, ExpiringSoonThreshold);

        Assert.Contains("анҷом меёбад", reason);
    }

    [Fact]
    public void Resolve_CredentialsExpiringExactlyAtThreshold_CountsAsExpiringSoon()
    {
        // <= threshold — марз худаш дохил аст.
        var reason = ChannelIssueReasonResolver.Resolve(
            requiresReconnect: false, webhookSetupWarning: null,
            credentialsExpiresAt: Now + ExpiringSoonThreshold, Now, ExpiringSoonThreshold);

        Assert.NotNull(reason);
    }

    [Fact]
    public void Resolve_CredentialsExpiringBeyondThreshold_NotMentioned()
    {
        var reason = ChannelIssueReasonResolver.Resolve(
            requiresReconnect: false, webhookSetupWarning: null,
            credentialsExpiresAt: Now.AddDays(11), Now, ExpiringSoonThreshold);

        Assert.Null(reason);
    }

    [Fact]
    public void Resolve_MultipleIssuesAtOnce_CombinesAllReasons()
    {
        var reason = ChannelIssueReasonResolver.Resolve(
            requiresReconnect: true, webhookSetupWarning: "Танзимот нотамом.",
            credentialsExpiresAt: Now.AddDays(1), Now, ExpiringSoonThreshold);

        Assert.NotNull(reason);
        Assert.Contains("Пайвастшавӣ лозим", reason);
        Assert.Contains("Танзимот нотамом.", reason);
        Assert.Contains("анҷом меёбад", reason);
    }
}
