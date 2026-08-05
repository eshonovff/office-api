using Office.Api.Auth;

namespace Office.Api.Tests.Auth;

public class PermissionResolverTests
{
    [Fact]
    public void Resolve_UnionsRolePermissions_WhenNoExceptions()
    {
        var rolePermissions = new[] { Permissions.Tasks.View, Permissions.Tasks.Create };

        var result = PermissionResolver.Resolve(rolePermissions, [], isOwner: false);

        Assert.Equal(new HashSet<string> { Permissions.Tasks.View, Permissions.Tasks.Create }, result);
    }

    [Fact]
    public void Resolve_AddsPersonalGrant_OnTopOfRolePermissions()
    {
        var rolePermissions = new[] { Permissions.Tasks.View };
        var exceptions = new[] { (Permissions.Tasks.Assign, true) };

        var result = PermissionResolver.Resolve(rolePermissions, exceptions, isOwner: false);

        Assert.Contains(Permissions.Tasks.View, result);
        Assert.Contains(Permissions.Tasks.Assign, result);
    }

    [Fact]
    public void Resolve_PersonalDeny_AlwaysOverridesRolePermission()
    {
        var rolePermissions = new[] { Permissions.Inbox.View, Permissions.Inbox.Reply, Permissions.Inbox.Close };
        var exceptions = new[] { (Permissions.Inbox.Close, false) };

        var result = PermissionResolver.Resolve(rolePermissions, exceptions, isOwner: false);

        Assert.Contains(Permissions.Inbox.View, result);
        Assert.Contains(Permissions.Inbox.Reply, result);
        Assert.DoesNotContain(Permissions.Inbox.Close, result);
    }

    [Fact]
    public void Resolve_Owner_BypassesEverythingAndGetsAllPermissions()
    {
        var exceptions = new[] { (Permissions.Users.Manage, false) };

        var result = PermissionResolver.Resolve([], exceptions, isOwner: true);

        Assert.Equal(Permissions.All, result);
    }
}
