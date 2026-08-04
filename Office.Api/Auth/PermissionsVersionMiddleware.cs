using System.IdentityModel.Tokens.Jwt;
using Microsoft.EntityFrameworkCore;
using Office.Api.Data;

namespace Office.Api.Auth;

public class PermissionsVersionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var sub = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            var pvClaim = context.User.FindFirst("pv")?.Value;

            if (sub is null || pvClaim is null ||
                !Guid.TryParse(sub, out var userId) ||
                !int.TryParse(pvClaim, out var tokenVersion))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            var currentVersion = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => (int?)u.PermissionsVersion)
                .FirstOrDefaultAsync();

            if (currentVersion is null || currentVersion != tokenVersion)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await next(context);
    }
}

public static class PermissionsVersionMiddlewareExtensions
{
    public static IApplicationBuilder UsePermissionsVersionCheck(this IApplicationBuilder app)
        => app.UseMiddleware<PermissionsVersionMiddleware>();
}
