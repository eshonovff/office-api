namespace Office.Api.Features.CustomerAuth;

/// <summary>Sends the links queued by POST /api/public/auth/forgot-password, one at a time.</summary>
public class PasswordResetWorker(
    PasswordResetQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<PasswordResetWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var email in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var sender = scope.ServiceProvider.GetRequiredService<PasswordResetSender>();
                await sender.SendAsync(email, DateTimeOffset.UtcNow, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Never log the email or anything about the account — only that one failed.
                logger.LogError(ex, "A password reset request failed");
            }
        }
    }
}
