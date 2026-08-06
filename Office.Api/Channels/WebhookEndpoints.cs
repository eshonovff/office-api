using Hangfire;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Office.Api.Data;
using Office.Api.Data.Entities;

namespace Office.Api.Channels;

public static class WebhookEndpoints
{
    public static IEndpointRouteBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/webhooks").WithTags("Webhooks");

        group.MapGet("/{provider}", VerifyAsync)
            .AllowAnonymous()
            .WithSummary("Тасдиқи webhook (hub.challenge) ҳангоми танзими subscription дар Meta");

        group.MapPost("/{provider}", ReceiveAsync)
            .AllowAnonymous()
            .WithSummary("Қабули webhook — санҷиши X-Hub-Signature-256, 200 фаврӣ, коркард дар Hangfire");

        return app;
    }

    private static IResult VerifyAsync(
        string provider,
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        IChannelProviderFactory factory)
    {
        if (!Enum.TryParse<ChannelType>(provider, ignoreCase: true, out var type))
            return Results.NotFound();

        var channelProvider = factory.GetProvider(type);

        if (mode != "subscribe" || string.IsNullOrEmpty(verifyToken) || string.IsNullOrEmpty(challenge) ||
            !channelProvider.VerifyWebhookToken(verifyToken))
        {
            return Results.Problem(
                title: "Токени тасдиқ нодуруст",
                detail: "hub.verify_token нодуруст аст.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Text(challenge);
    }

    private static async Task<IResult> ReceiveAsync(
        string provider,
        HttpRequest request,
        AppDbContext db,
        IConfiguration configuration,
        IBackgroundJobClient backgroundJobs,
        CancellationToken ct)
    {
        if (!Enum.TryParse<ChannelType>(provider, ignoreCase: true, out _))
            return Results.NotFound();

        request.EnableBuffering();
        string rawBody;
        using (var reader = new StreamReader(request.Body, leaveOpen: true))
            rawBody = await reader.ReadToEndAsync(ct);
        request.Body.Position = 0;

        var signature = request.Headers["X-Hub-Signature-256"].ToString();
        var appSecret = configuration["Webhooks:AppSecret"]
            ?? throw new InvalidOperationException("Webhooks:AppSecret танзим нашудааст.");

        if (!WebhookSignature.IsValid(rawBody, signature, appSecret))
        {
            return Results.Problem(
                title: "Имзои нодуруст",
                detail: "X-Hub-Signature-256 нодуруст аст.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        var log = new WebhookLog
        {
            Id = Guid.CreateVersion7(),
            Provider = provider,
            RawJson = rawBody,
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        db.WebhookLogs.Add(log);
        await db.SaveChangesAsync(ct);

        backgroundJobs.Enqueue<WebhookProcessor>(p => p.ProcessAsync(log.Id, CancellationToken.None));

        return Results.Ok();
    }
}
