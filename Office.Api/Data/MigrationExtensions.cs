using Microsoft.EntityFrameworkCore;

namespace Office.Api.Data;

public static class MigrationExtensions
{
    // Калиди собит барои pg_advisory_lock — то ду instance ҳамзамон migrate накунанд.
    private const long MigrationLockKey = 8_112_026;

    public static async Task ApplyMigrationsAsync(this WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Migrations");

        try
        {
            await ApplyPendingMigrationsAsync(db, logger);
            await DbSeeder.SeedAsync(db, app.Configuration);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Migration or seed failed. The application will not start.");
            throw;
        }
    }

    private static async Task ApplyPendingMigrationsAsync(AppDbContext db, ILogger logger)
    {
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("No pending migrations.");
            return;
        }

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync();
        try
        {
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.CommandText = $"SELECT pg_advisory_lock({MigrationLockKey})";
                await lockCommand.ExecuteNonQueryAsync();
            }

            // Дубора санҷем: то замони гирифтани lock, instance-и дигар шояд аллакай migrate карда бошад.
            pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            if (pending.Count == 0)
            {
                logger.LogInformation("No pending migrations left (applied by another instance while waiting for the lock).");
                return;
            }

            logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
            await db.Database.MigrateAsync();
            logger.LogInformation("Migrations applied successfully.");
        }
        finally
        {
            await using var unlockCommand = connection.CreateCommand();
            unlockCommand.CommandText = $"SELECT pg_advisory_unlock({MigrationLockKey})";
            await unlockCommand.ExecuteNonQueryAsync();
            await connection.CloseAsync();
        }
    }
}
