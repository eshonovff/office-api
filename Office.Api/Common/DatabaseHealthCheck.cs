using Microsoft.Extensions.Diagnostics.HealthChecks;
using Office.Api.Data;

namespace Office.Api.Common;

public class DatabaseHealthCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var canConnect = await db.Database.CanConnectAsync(cancellationToken);
            return canConnect
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("База дастрас нест.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("База дастрас нест.", ex);
        }
    }
}
