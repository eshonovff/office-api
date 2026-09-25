using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Tests.Auth;

/// <summary>Who is told about a new receipt: exactly the staff who may approve it.</summary>
public class StaffWithPermissionTests
{
    private const string Manage = Permissions.Subscriptions.Manage;

    private readonly AppDbContext _db =
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private Role AddRole(string key, params string[] permissions)
    {
        var role = new Role { Id = Guid.NewGuid(), Key = key, Name = key };
        _db.Roles.Add(role);
        foreach (var p in permissions)
            _db.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = p });
        return role;
    }

    private Guid AddUser(string name, Role? role = null, bool active = true, (string Key, bool Granted)? exception = null)
    {
        var user = new User { Id = Guid.NewGuid(), FullName = name, Username = name, PasswordHash = "x", IsActive = active };
        _db.Users.Add(user);
        if (role is not null)
            _db.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = role.Id });
        if (exception is { } e)
            _db.UserPermissions.Add(new UserPermission { UserId = user.Id, PermissionKey = e.Key, IsGranted = e.Granted });
        return user.Id;
    }

    [Fact]
    public async Task FindsExactlyThoseWhoMayApprove()
    {
        var owner = AddRole(RoleKeys.Owner);                 // no explicit keys — Owner has everything
        var admin = AddRole(RoleKeys.Admin, Manage);
        var moderator = AddRole("moderator", Manage);
        var operatorRole = AddRole("operator", Permissions.Inbox.View);

        var ownerUser = AddUser("owner", owner);
        var adminUser = AddUser("admin", admin);
        var moderatorUser = AddUser("moderator", moderator);
        var personallyGranted = AddUser("granted", operatorRole, exception: (Manage, true));
        AddUser("operator", operatorRole);                                   // no permission
        AddUser("denied", moderator, exception: (Manage, false));             // denial beats the role
        AddUser("inactive", moderator, active: false);                        // switched off
        AddUser("no-role");
        await _db.SaveChangesAsync();

        var ids = await StaffWithPermission.FindUserIdsAsync(_db, Manage, CancellationToken.None);

        Assert.Equal(
            new[] { ownerUser, adminUser, moderatorUser, personallyGranted }.Order(),
            ids.Order());
    }
}
