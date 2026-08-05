using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Office.Api.Common;
using Office.Api.Realtime;

namespace Office.Api.Tests.Realtime;

public class BoardHubTests
{
    private sealed class FakeProjectAccessGuard(bool result) : IProjectAccessGuard
    {
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, Guid projectId, CancellationToken ct)
            => Task.FromResult(result);
    }

    private static BoardHub CreateHub(string? projectIdQueryValue, IProjectAccessGuard access, out FakeHubCallerContext context, out FakeGroupManager groups)
    {
        var httpContext = new DefaultHttpContext();
        if (projectIdQueryValue is not null)
            httpContext.Request.QueryString = new QueryString($"?projectId={projectIdQueryValue}");

        context = new FakeHubCallerContext { UserOverride = new ClaimsPrincipal(new ClaimsIdentity()) };
        context.Features.Set<Microsoft.AspNetCore.Http.Connections.Features.IHttpContextFeature>(
            new FakeHttpContextFeature { HttpContext = httpContext });

        groups = new FakeGroupManager();

        return new BoardHub(access) { Context = context, Groups = groups };
    }

    [Fact]
    public async Task OnConnectedAsync_MemberOfProject_JoinsGroup()
    {
        var projectId = Guid.NewGuid();
        var hub = CreateHub(projectId.ToString(), new FakeProjectAccessGuard(result: true), out var context, out var groups);

        await hub.OnConnectedAsync();

        Assert.False(context.Aborted);
        Assert.Single(groups.Added);
        Assert.Equal(BoardHub.GroupName(projectId), groups.Added[0].GroupName);
    }

    [Fact]
    public async Task OnConnectedAsync_NotAMember_AbortsAndNeverJoinsGroup()
    {
        var projectId = Guid.NewGuid();
        var hub = CreateHub(projectId.ToString(), new FakeProjectAccessGuard(result: false), out var context, out var groups);

        await hub.OnConnectedAsync();

        Assert.True(context.Aborted);
        Assert.Empty(groups.Added);
    }

    [Fact]
    public async Task OnConnectedAsync_MissingOrInvalidProjectId_AbortsWithoutCheckingAccess()
    {
        // Агар access-check ҳатто дуруст бошад ҳам, бе projectId-и дуруст набояд гурӯҳе ҳамроҳ шавад.
        var hub = CreateHub(projectIdQueryValue: "not-a-guid", new FakeProjectAccessGuard(result: true), out var context, out var groups);

        await hub.OnConnectedAsync();

        Assert.True(context.Aborted);
        Assert.Empty(groups.Added);
    }
}
