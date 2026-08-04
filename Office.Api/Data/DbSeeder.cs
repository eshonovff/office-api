using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Data.Entities;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration configuration, CancellationToken ct = default)
    {
        await SeedRolesAsync(db, ct);
        await SeedOwnerAsync(db, configuration, ct);
    }

    private static async Task SeedRolesAsync(AppDbContext db, CancellationToken ct)
    {
        (string Key, string Name, IReadOnlyCollection<string> PermissionKeys)[] definitions =
        [
            (RoleKeys.Owner, "Соҳиб", Permissions.All),
            (
                RoleKeys.Admin,
                "Админ",
                Permissions.All.Except([Permissions.Users.Manage, Permissions.Roles.Manage]).ToList()
            ),
            (
                RoleKeys.Developer,
                "Барномасоз",
                [Permissions.Projects.View, Permissions.Tasks.View, Permissions.Tasks.Create, Permissions.Tasks.Edit, Permissions.Tasks.Move]
            ),
            (
                RoleKeys.Operator,
                "Оператор",
                [Permissions.Inbox.View, Permissions.Inbox.Reply, Permissions.Inbox.Close]
            ),
            (
                RoleKeys.Manager,
                "Менеҷер",
                [
                    Permissions.Projects.View,
                    Permissions.Tasks.View, Permissions.Tasks.Create, Permissions.Tasks.Assign,
                    Permissions.Inbox.View, Permissions.Inbox.Reply, Permissions.Inbox.Assign, Permissions.Inbox.Close,
                    Permissions.Templates.Manage,
                ]
            ),
        ];

        foreach (var def in definitions)
        {
            var role = await db.Roles
                .Include(r => r.RolePermissions)
                .FirstOrDefaultAsync(r => r.Key == def.Key, ct);

            if (role is null)
            {
                role = new Role { Id = Guid.CreateVersion7(), Key = def.Key, Name = def.Name, IsSystem = true };
                db.Roles.Add(role);
            }

            var existingKeys = role.RolePermissions.Select(rp => rp.PermissionKey).ToHashSet();
            foreach (var permissionKey in def.PermissionKeys.Where(pk => !existingKeys.Contains(pk)))
                role.RolePermissions.Add(new RolePermission { RoleId = role.Id, PermissionKey = permissionKey });
        }

        await db.SaveChangesAsync(ct);
    }

    private static async Task SeedOwnerAsync(AppDbContext db, IConfiguration configuration, CancellationToken ct)
    {
        var ownerExists = await db.Users.AnyAsync(u => u.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner), ct);
        if (ownerExists)
            return;

        var password = configuration["Seed:OwnerPassword"]
            ?? throw new InvalidOperationException("Seed:OwnerPassword танзим нашудааст.");

        var ownerRole = await db.Roles.FirstAsync(r => r.Key == RoleKeys.Owner, ct);

        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            FullName = "Owner",
            Username = "owner",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            IsActive = true,
            MustChangePassword = true,
            PermissionsVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        owner.UserRoles.Add(new UserRole { UserId = owner.Id, RoleId = ownerRole.Id });
        db.Users.Add(owner);

        await db.SaveChangesAsync(ct);
    }
}
