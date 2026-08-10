using Office.Api.Media;

namespace Office.Api.Tests.Media;

public class MediaAccessDecisionTests
{
    [Fact]
    public void Evaluate_MessageNotFound_ReturnsNotFound()
    {
        var outcome = MediaAccessDecision.Evaluate(
            messageFound: false, hasAccess: true, storedRelativePath: "some/path.jpg", downloadError: null, deletedAt: null);

        Assert.Equal(MediaAccessOutcome.NotFound, outcome);
    }

    [Fact]
    public void Evaluate_AccessDenied_ReturnsNotFound()
    {
        // 404, not 403 — matches the existing ChannelAccessGuard convention of not revealing existence.
        var outcome = MediaAccessDecision.Evaluate(
            messageFound: true, hasAccess: false, storedRelativePath: "some/path.jpg", downloadError: null, deletedAt: null);

        Assert.Equal(MediaAccessOutcome.NotFound, outcome);
    }

    [Fact]
    public void Evaluate_DownloadError_ReturnsDownloadFailed_EvenIfPathIsSet()
    {
        var outcome = MediaAccessDecision.Evaluate(
            messageFound: true, hasAccess: true, storedRelativePath: "some/path.jpg", downloadError: "timed out", deletedAt: null);

        Assert.Equal(MediaAccessOutcome.DownloadFailed, outcome);
    }

    [Fact]
    public void Evaluate_DeletedAt_ReturnsDeleted()
    {
        var outcome = MediaAccessDecision.Evaluate(
            messageFound: true, hasAccess: true, storedRelativePath: "some/path.jpg", downloadError: null,
            deletedAt: DateTimeOffset.UtcNow);

        Assert.Equal(MediaAccessOutcome.Deleted, outcome);
    }

    [Fact]
    public void Evaluate_DownloadErrorTakesPriorityOverDeletedAt()
    {
        var outcome = MediaAccessDecision.Evaluate(
            messageFound: true, hasAccess: true, storedRelativePath: "some/path.jpg", downloadError: "timed out",
            deletedAt: DateTimeOffset.UtcNow);

        Assert.Equal(MediaAccessOutcome.DownloadFailed, outcome);
    }

    [Fact]
    public void Evaluate_NoStoredPath_ReturnsNotFound()
    {
        var outcome = MediaAccessDecision.Evaluate(
            messageFound: true, hasAccess: true, storedRelativePath: null, downloadError: null, deletedAt: null);

        Assert.Equal(MediaAccessOutcome.NotFound, outcome);
    }

    [Fact]
    public void Evaluate_AllClear_ReturnsReady()
    {
        var outcome = MediaAccessDecision.Evaluate(
            messageFound: true, hasAccess: true, storedRelativePath: "some/path.jpg", downloadError: null, deletedAt: null);

        Assert.Equal(MediaAccessOutcome.Ready, outcome);
    }
}
