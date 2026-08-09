using Microsoft.AspNetCore.SignalR;
using Office.Api.Realtime;

namespace Office.Api.Tests.Realtime;

public class FakeClientProxy : IClientProxy
{
    public List<(string Method, object?[] Args)> Sent { get; } = [];

    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
    {
        Sent.Add((method, args));
        return Task.CompletedTask;
    }
}

public class FakeHubClients : IHubClients
{
    public Dictionary<string, FakeClientProxy> GroupProxies { get; } = [];

    public IClientProxy Group(string groupName) =>
        GroupProxies.TryGetValue(groupName, out var proxy) ? proxy : GroupProxies[groupName] = new FakeClientProxy();

    public IClientProxy All => throw new NotSupportedException();
    public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
    public IClientProxy Client(string connectionId) => throw new NotSupportedException();
    public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
    public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
    public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
    public IClientProxy OthersInGroup(string groupName) => throw new NotSupportedException();
    public IClientProxy User(string userId) => throw new NotSupportedException();
    public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
}

public class FakeHubContext : IHubContext<InboxHub>
{
    public FakeHubClients ClientsImpl { get; } = new();

    public IHubClients Clients => ClientsImpl;
    public IGroupManager Groups => throw new NotSupportedException();
}
