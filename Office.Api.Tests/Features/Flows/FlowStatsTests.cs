using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Channels.Flows;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Features.Flows;

namespace Office.Api.Tests.Features.Flows;

/// <summary>
/// The builder's numbers (GET …/flows/{id}/stats), called as мизоҷ A: sessions by state, each
/// "next" button's clicks (each person once) and the goal — B's flow is "not found".
/// </summary>
public class FlowStatsTests
{
    private sealed class FixedTenant(TenantIdentity identity) : ITenantContext
    {
        public TenantIdentity Current => identity;
    }

    private readonly string _databaseName = Guid.NewGuid().ToString();
    private readonly Guid _customerA = Guid.NewGuid();
    private readonly Guid _flowA = Guid.NewGuid();
    private readonly Guid _flowB = Guid.NewGuid();
    private readonly Guid _message = Guid.NewGuid();

    public FlowStatsTests()
    {
        using var db = Open(null);
        var customerB = Guid.NewGuid();
        var channelA = Guid.NewGuid();
        var channelB = Guid.NewGuid();
        db.Customers.AddRange(
            new Customer { Id = _customerA, Email = "a@example.com", FullName = "A" },
            new Customer { Id = customerB, Email = "b@example.com", FullName = "B" });
        db.Channels.AddRange(
            new Channel { Id = channelA, Type = ChannelType.Instagram, Name = "a", ExternalId = "a", CustomerId = _customerA, IsActive = true },
            new Channel { Id = channelB, Type = ChannelType.Instagram, Name = "b", ExternalId = "b", CustomerId = customerB, IsActive = true });
        var contacts = Enumerable.Range(0, 3).Select(i => new Conversation { Id = Guid.NewGuid(), ChannelId = channelA, ExternalId = $"p{i}" }).ToList();
        db.Conversations.AddRange(contacts);
        db.Flows.AddRange(Flow(_flowA, channelA), Flow(_flowB, channelB));
        db.FlowNodes.Add(new FlowNode { Id = _message, FlowId = _flowA, Type = FlowNodeType.Message, ConfigJson = "{}" });

        var statuses = new[] { FlowSessionStatus.Finished, FlowSessionStatus.Waiting, FlowSessionStatus.Failed };
        var sessions = contacts.Select((c, i) => new FlowSession
        {
            Id = Guid.NewGuid(), FlowId = _flowA, ContactId = c.Id, Status = statuses[i], CreatedAt = DateTimeOffset.UtcNow,
        }).ToList();
        db.FlowSessions.AddRange(sessions);
        db.FlowSessionSteps.AddRange(sessions.Select(s => Step(s.Id, null)));
        db.FlowSessionSteps.AddRange(
            Step(sessions[0].Id, FlowEngine.ButtonPort(0)), Step(sessions[0].Id, FlowEngine.ButtonPort(0)), // clicked twice — one person
            Step(sessions[1].Id, FlowEngine.ButtonPort(0)),
            Step(sessions[2].Id, FlowEngine.ButtonPort(1)));
        db.FlowConversions.Add(new FlowConversion { FlowId = _flowA, ContactId = contacts[0].Id, CreatedAt = DateTimeOffset.UtcNow });
        db.SaveChanges();
    }

    private static Flow Flow(Guid id, Guid channelId) => new()
    {
        Id = id, ChannelId = channelId, Name = "f", TriggerType = "instagram_dm", TriggerConfigJson = "{}",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private FlowSessionStep Step(Guid sessionId, string? port) =>
        new() { Id = Guid.NewGuid(), SessionId = sessionId, NodeId = _message, FromPort = port, CreatedAt = DateTimeOffset.UtcNow };

    private AppDbContext Open(TenantIdentity? tenant) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_databaseName).Options,
        tenant is null ? null : new FixedTenant(tenant.Value));

    [Fact]
    public async Task Stats_CountStates_ButtonsOncePerPerson_AndTheGoal()
    {
        await using var db = Open(new TenantIdentity(TenantScope.Customer, _customerA));

        var stats = Assert.IsType<Ok<FlowStats>>(await FlowsEndpoints.StatsAsync(_flowA, db, CancellationToken.None)).Value!;

        Assert.Equal((3, 1, 1, 1), (stats.TotalSessions, stats.FinishedSessions, stats.ActiveOrWaitingSessions, stats.FailedSessions));
        Assert.Equal(3, Assert.Single(stats.Nodes).ContactCount); // the clicks don't add people to the node
        Assert.Equal(
            [new FlowButtonStat(_message, 0, 2), new FlowButtonStat(_message, 1, 1)],
            stats.Buttons.OrderBy(b => b.ButtonIndex));
        Assert.Equal(1, stats.Conversions);
    }

    [Fact]
    public async Task Stats_OfAnotherMizojsFlow_IsNotFound()
    {
        await using var db = Open(new TenantIdentity(TenantScope.Customer, _customerA));

        var result = await FlowsEndpoints.StatsAsync(_flowB, db, CancellationToken.None);

        Assert.Equal(404, (result as IStatusCodeHttpResult)?.StatusCode);
    }
}
