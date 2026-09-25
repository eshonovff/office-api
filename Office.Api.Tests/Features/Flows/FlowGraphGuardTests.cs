using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels.Flows;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Flows;

namespace Office.Api.Tests.Features.Flows;

/// <summary>A graph save by мизоҷ A (tenant-filtered, like a real request).</summary>
public class FlowGraphGuardTests
{
    private static readonly Guid MizojA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid MizojB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Flow _flowA;          // the flow being saved — мизоҷ A, channel A1
    private readonly Flow _siblingA;       // мизоҷ A, same channel A1
    private readonly Flow _otherChannelA;  // мизоҷ A, their second channel A2
    private readonly Flow _flowB;          // мизоҷ B
    private readonly Guid _nodeOfB = Guid.NewGuid();
    private readonly Guid _nodeOfFlowA = Guid.NewGuid();

    public FlowGraphGuardTests()
    {
        using var db = Open(tenant: null);
        Channel Channel(Guid? owner, string ext) =>
            new() { Id = Guid.NewGuid(), Type = ChannelType.Instagram, Name = ext, ExternalId = ext, CustomerId = owner };
        Flow FlowOn(Channel c) => new()
        {
            Id = Guid.NewGuid(), ChannelId = c.Id, Name = "f", TriggerType = "instagram_dm", TriggerConfigJson = "{}",
        };

        var a1 = Channel(MizojA, "a1");
        var a2 = Channel(MizojA, "a2");
        var b1 = Channel(MizojB, "b1");
        db.Channels.AddRange(a1, a2, b1);
        _flowA = FlowOn(a1);
        _siblingA = FlowOn(a1);
        _otherChannelA = FlowOn(a2);
        _flowB = FlowOn(b1);
        db.Flows.AddRange(_flowA, _siblingA, _otherChannelA, _flowB);
        db.FlowNodes.AddRange(
            new FlowNode { Id = _nodeOfB, FlowId = _flowB.Id, Type = FlowNodeType.Note, ConfigJson = "{}" },
            new FlowNode { Id = _nodeOfFlowA, FlowId = _flowA.Id, Type = FlowNodeType.Note, ConfigJson = "{}" });
        db.SaveChanges();
    }

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    private static FlowNodeInput GotoNode(Guid target) => new(
        Guid.NewGuid(), "action",
        JsonSerializer.SerializeToElement(new ActionNodeConfig(ActionNodeConfig.KindGotoFlow, TargetFlowId: target), FlowJsonOptions.Options),
        0, 0);

    private static FlowNodeInput NoteNode(Guid id) => new(id, "note", JsonSerializer.SerializeToElement(new { text = "x" }), 0, 0);

    private async Task<int?> Check(params FlowNodeInput[] nodes)
    {
        await using var db = Open(new TenantIdentity(TenantScope.Customer, MizojA));
        var result = await FlowGraphGuard.CheckAsync(_flowA, new UpdateFlowGraphRequest(nodes, []), db, CancellationToken.None);
        return (result as IStatusCodeHttpResult)?.StatusCode;
    }

    [Fact]
    public async Task GotoAFlowOnTheSameChannel_IsAllowed()
    {
        Assert.Null(await Check(GotoNode(_siblingA.Id)));
    }

    [Fact]
    public async Task GotoAnotherMizojsFlow_IsRefused()
    {
        Assert.Equal(StatusCodes.Status400BadRequest, await Check(GotoNode(_flowB.Id)));
    }

    [Fact]
    public async Task GotoOwnFlowOnAnotherChannel_IsRefused()
    {
        // Still the same owner — but the contact belongs to this channel only.
        Assert.Equal(StatusCodes.Status400BadRequest, await Check(GotoNode(_otherChannelA.Id)));
    }

    [Fact]
    public async Task NodeIdOfAnotherOwnersFlow_IsAConflict_NotA500()
    {
        Assert.Equal(StatusCodes.Status409Conflict, await Check(NoteNode(_nodeOfB)));
    }

    [Fact]
    public async Task ResavingTheFlowsOwnNodeIds_IsAllowed()
    {
        Assert.Null(await Check(NoteNode(_nodeOfFlowA)));
    }
}
