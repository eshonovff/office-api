using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;

namespace Office.Api.Tests.Realtime;

public class FakeHttpContextFeature : Microsoft.AspNetCore.Http.Connections.Features.IHttpContextFeature
{
    public Microsoft.AspNetCore.Http.HttpContext? HttpContext { get; set; }
}

public class FakeHubCallerContext : HubCallerContext
{
    public bool Aborted { get; private set; }

    public override string ConnectionId { get; } = Guid.NewGuid().ToString();

    public override string? UserIdentifier => null;

    public ClaimsPrincipal? UserOverride { get; set; }

    public override ClaimsPrincipal? User => UserOverride;

    public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();

    public override IFeatureCollection Features { get; } = new FeatureCollection();

    public override CancellationToken ConnectionAborted => CancellationToken.None;

    public override void Abort() => Aborted = true;
}
