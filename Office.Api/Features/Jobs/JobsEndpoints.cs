using Hangfire;
using Office.Api.Auth;
using Office.Api.Channels.ContactProfiles;

namespace Office.Api.Features.Jobs;

/// <summary>
/// /hangfire дар dev бо ?access_token=&lt;JWT&gt; кушода мешавад, вале токен дар браузер танҳо дар
/// хотира зинда аст ва пас аз 15 дақиқа мемирад — барои триггери такрории як job ин роҳи содда нест.
/// Ин endpoint-ҳо ҳамон job-ҳоро (танҳо media-maintenance, як маротибаина/идемпотентӣ) бе dashboard
/// оғоз мекунанд, бо ҳамон дари RoleKeys.Owner мисли OwnerOnlyDashboardAuthFilter.
/// </summary>
public static class JobsEndpoints
{
    public static IEndpointRouteBuilder MapJobsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/jobs").WithTags("Jobs").RequireOwner();

        group.MapPost("/contact-profile-backfill", TriggerContactProfileBackfill)
            .WithSummary("Дастӣ оғоз кардани ContactProfileBackfillJob — номҳо ва суратҳои контактҳо (алтернатива ба Hangfire dashboard)")
            .Produces<TriggerJobResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        return app;
    }

    private static IResult TriggerContactProfileBackfill()
    {
        var jobId = BackgroundJob.Enqueue<ContactProfileBackfillJob>(j => j.RunAsync(CancellationToken.None));
        return Results.Accepted(value: new TriggerJobResponse(jobId));
    }
}

public record TriggerJobResponse(string JobId);
