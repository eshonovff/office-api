using Office.Api.Features.Subscriptions;

namespace Office.Api.Tests.Features.Subscriptions;

public class PaymentWindowTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(5);

    [Fact]
    public void DeadlineFor_IsCreatedAtPlusWindow()
    {
        Assert.Equal(CreatedAt.AddMinutes(5), PaymentWindow.DeadlineFor(CreatedAt, Window));
    }

    [Fact]
    public void IsPastDeadline_FalseUpToTheDeadlineItself()
    {
        Assert.False(PaymentWindow.IsPastDeadline(CreatedAt, Window, CreatedAt.AddMinutes(4)));
        Assert.False(PaymentWindow.IsPastDeadline(CreatedAt, Window, CreatedAt.AddMinutes(5)));
    }

    [Fact]
    public void IsPastDeadline_TrueRightAfterIt()
    {
        Assert.True(PaymentWindow.IsPastDeadline(CreatedAt, Window, CreatedAt.AddMinutes(5).AddSeconds(1)));
    }

    [Fact]
    public void IsTooLateToUpload_AllowsTheGraceAfterTheDeadline()
    {
        // 30s past the deadline the customer saw — an upload that started in time.
        Assert.False(PaymentWindow.IsTooLateToUpload(CreatedAt, Window, CreatedAt.AddMinutes(5).AddSeconds(30)));
    }

    [Fact]
    public void IsTooLateToUpload_TrueOnceTheGraceIsOver()
    {
        Assert.True(PaymentWindow.IsTooLateToUpload(CreatedAt, Window, CreatedAt.AddMinutes(6).AddSeconds(1)));
    }
}
