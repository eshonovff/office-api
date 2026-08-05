using Microsoft.EntityFrameworkCore;
using Office.Api.Data;

namespace Office.Api.Realtime;

/// <summary>
/// Огоҳии "deadline фардо" — тавассути BackgroundService-и оддии .NET, на Hangfire
/// (Hangfire барои фазаи 4 нигоҳ дошта шудааст, тибқи docs/01-architecture.md).
/// </summary>
public class DeadlineNotificationBackgroundService(
    IServiceScopeFactory scopeFactory,
    ILogger<DeadlineNotificationBackgroundService> logger) : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CheckInterval);
        do
        {
            try
            {
                await CheckDeadlinesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Deadline notification check failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CheckDeadlinesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var utcNow = DateTimeOffset.UtcNow;
        var tomorrow = new DateTimeOffset(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, TimeSpan.Zero).AddDays(1);
        var dayAfter = tomorrow.AddDays(1);

        var dueTasks = await db.Tasks
            .Where(t => t.AssigneeId != null && t.DueDate >= tomorrow && t.DueDate < dayAfter)
            .Select(t => new { t.Id, t.Title, AssigneeId = t.AssigneeId!.Value })
            .ToListAsync(ct);

        foreach (var task in dueTasks)
        {
            var alreadyNotified = await db.Notifications.AnyAsync(n =>
                n.UserId == task.AssigneeId &&
                n.Type == "deadline_tomorrow" &&
                n.PayloadJson != null && n.PayloadJson.Contains(task.Id.ToString()), ct);

            if (alreadyNotified)
                continue;

            await notifications.PushAsync(
                task.AssigneeId, "deadline_tomorrow", new { taskId = task.Id, title = task.Title }, ct);
        }
    }
}
