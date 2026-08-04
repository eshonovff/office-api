using Office.Api.Auth;
using Office.Api.Data.Entities;

namespace Office.Api.Features.Auth;

public record LoginRequest(string Username, string Password);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record MeResponse(
    Guid Id,
    string FullName,
    string Username,
    bool MustChangePassword,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Permissions)
{
    public static MeResponse From(User user, PermissionResolution permissions) => new(
        user.Id,
        user.FullName,
        user.Username,
        user.MustChangePassword,
        permissions.Roles,
        permissions.Permissions.ToList());
}

public record LoginResponse(string AccessToken, bool MustChangePassword, MeResponse User);
