using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.OpenApi;
using Office.Api.Auth;
using Office.Api.Channels;
using Office.Api.Channels.Facebook;
using Office.Api.Channels.Instagram;
using Office.Api.Channels.Meta;
using Office.Api.Channels.WhatsApp;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Media;
using Office.Api.Features.Auth;
using Office.Api.Features.Channels;
using Office.Api.Features.Conversations;
using Office.Api.Features.Legal;
using Office.Api.Features.Notifications;
using Office.Api.Features.Projects;
using Office.Api.Features.Roles;
using Office.Api.Features.Tasks;
using Office.Api.Features.Users;
using Office.Api.Realtime;
using Office.Api.Sms;
using Scalar.AspNetCore;
using Serilog;

const string FrontendCorsPolicy = "Frontend";
const string LoginRateLimiterPolicy = "login";

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddProblemDetails();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "office.nizom.tj API";
        document.Info.Version = "v1";
        document.Info.Description =
            "Backend-и платформаи дохилии SMARTWEB TJ — кормандон, доступ, проект/таск. " +
            "Барои санҷиш: аввал /api/auth/login даъват кун, баъд accessToken-ро дар тугмаи " +
            "\"Authorize\" (боло) гузор.";

        document.Tags = new HashSet<OpenApiTag>
        {
            new() { Name = "Auth", Description = "Воридшавӣ, refresh, logout, тағйири парол" },
            new() { Name = "Users", Description = "Идораи корманд, роль ва иҷозати шахсӣ" },
            new() { Name = "Roles", Description = "Идораи роль ва рӯйхати permission-ҳо" },
            new() { Name = "Projects", Description = "Проект ва аъзои он" },
            new() { Name = "Columns", Description = "Колонкаҳои board-и проект" },
            new() { Name = "Tasks", Description = "Таск, board ва кӯчонидан (drag & drop)" },
            new() { Name = "Comments", Description = "Комментарии таск" },
            new() { Name = "Attachments", Description = "Файли замимаи таск" },
            new() { Name = "Labels", Description = "Тегҳои проект" },
            new() { Name = "Activity", Description = "Таърихи тағйироти таск" },
        };

        return Task.CompletedTask;
    });
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
});

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres");

var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"] ?? "/var/office/keys";
builder.Services.AddDataProtection()
    .SetApplicationName("office-api")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

var hangfireConnectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("ConnectionStrings:Default танзим нашудааст.");

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(hangfireConnectionString)));
builder.Services.AddHangfireServer();

// Коркарди медиа (transcode/thumbnail — CPU вазнин) дар навбати ҷудогонаи
// маҳдуд, то якчанд боркунии ҳамзамон CPU-и серверро банд накунад.
builder.Services.AddHangfireServer(options =>
{
    options.ServerName = "media-worker";
    options.Queues = ["media"];
    options.WorkerCount = 2;
});

var corsAllowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
if (corsAllowedOrigins is not { Length: > 0 })
    throw new InvalidOperationException("Cors:AllowedOrigins танзим нашудааст.");

builder.Services.AddCors(options =>
{
    // Ҳеҷ гоҳ AllowAnyOrigin — AllowCredentials фаъол аст, ва wildcard бо credentials
    // сӯрохи амниятии воқеӣ мешавад. Origin-ҳо аз конфигуратсия (appsettings/env), на
    // hardcode — то dev/tunnel/prod бе тағйири код кор кунанд.
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins(corsAllowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key танзим нашудааст.");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero,
        };

        // WebSocket-и браузер Authorization header гузошта наметавонад — токенро
        // барои hub-ҳои SignalR аз query string мегирем.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                if (!string.IsNullOrEmpty(accessToken) &&
                    (path.StartsWithSegments("/hubs/board") || path.StartsWithSegments("/hubs/inbox") ||
                     path.StartsWithSegments("/hangfire")))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(LoginRateLimiterPolicy, context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromMinutes(1),
                PermitLimit = 5,
                QueueLimit = 0,
            }));
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();

builder.Services.AddSignalR();

builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ProjectAccessGuard>();
builder.Services.AddScoped<IProjectAccessGuard>(sp => sp.GetRequiredService<ProjectAccessGuard>());
builder.Services.AddScoped<ChannelAccessGuard>();
builder.Services.AddScoped<IChannelAccessGuard>(sp => sp.GetRequiredService<ChannelAccessGuard>());
builder.Services.AddScoped<IBoardEventPublisher, BoardEventPublisher>();
builder.Services.AddScoped<IInboxEventPublisher, InboxEventPublisher>();
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHostedService<DeadlineNotificationBackgroundService>();

builder.Services.AddSingleton<IChannelCredentialsProtector, ChannelCredentialsProtector>();
builder.Services.AddSingleton<IMediaProcessor, FfmpegMediaProcessor>();
builder.Services.AddHttpClient<WhatsAppProvider>();
builder.Services.AddHttpClient<FacebookProvider>();
builder.Services.AddHttpClient<InstagramProvider>();
builder.Services.AddScoped<IChannelProviderFactory, ChannelProviderFactory>();
// RemoveAllLoggers(): URL-и дархостҳо ба Meta code/token-ро дар query string доранд —
// logging handler-и пешфарзи HttpClientFactory набояд онҳоро ба log бароварад.
builder.Services.AddHttpClient<FacebookOAuthConnector>().RemoveAllLoggers();
builder.Services.AddHttpClient<InstagramOAuthConnector>().RemoveAllLoggers();
builder.Services.AddScoped<IChannelOAuthConnectorFactory, ChannelOAuthConnectorFactory>();
builder.Services.AddSingleton<IOAuthNonceTracker, OAuthNonceTracker>();
builder.Services.AddSingleton<IOAuthConnectionStore, OAuthConnectionStore>();
builder.Services.AddScoped<WebhookProcessor>();
builder.Services.AddScoped<WebhookLogCleanupJob>();
builder.Services.AddScoped<WhatsAppSendJob>();
builder.Services.AddScoped<MediaDownloadJob>();
builder.Services.AddScoped<MediaSendJob>();
builder.Services.AddScoped<MediaRetentionCleanupJob>();
builder.Services.AddScoped<WaveformBackfillJob>();
builder.Services.AddScoped<ConversationAutoReleaseJob>();
builder.Services.AddScoped<HtmlMediaCleanupJob>();

builder.Services.AddHttpClient<ISmsSender, OsonSmsSender>();

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var isClientInputError = error is not null && ClientErrorClassifier.IsClientInputError(error);

    context.Response.StatusCode = isClientInputError
        ? StatusCodes.Status400BadRequest
        : StatusCodes.Status500InternalServerError;

    var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
    await problemDetailsService.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails = isClientInputError
            ? new()
            {
                Title = "Дархости нодуруст",
                Detail = "Формати маълумоти фиристодашуда нодуруст аст.",
                Status = StatusCodes.Status400BadRequest,
            }
            : new()
            {
                Title = "Хатогии сервер",
                Detail = "Дар сервер хатогии дохилӣ рӯй дод.",
                Status = StatusCodes.Status500InternalServerError,
            },
    });
}));

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options => options
        .WithTitle("office.nizom.tj API")
        .AddPreferredSecuritySchemes(["Bearer"])
        .EnablePersistentAuthentication());
}

app.UseCors(FrontendCorsPolicy);
app.UseRateLimiter();

app.UseAuthentication();
app.UsePermissionsVersionCheck();
app.UseAuthorization();

app.MapHealthChecks("/health");

app.MapAuthEndpoints();
app.MapUsersEndpoints();
app.MapRolesEndpoints();
app.MapProjectsEndpoints();
app.MapColumnsEndpoints();
app.MapLabelsEndpoints();
app.MapTasksEndpoints();
app.MapCommentsEndpoints();
app.MapAttachmentsEndpoints();
app.MapActivityEndpoints();
app.MapNotificationsEndpoints();
app.MapChannelsEndpoints();
app.MapChannelOAuthEndpoints();
app.MapWebhookEndpoints();
app.MapLegalEndpoints();
app.MapConversationsEndpoints();
app.MapMessagesEndpoints();

app.MapHub<BoardHub>("/hubs/board");
app.MapHub<InboxHub>("/hubs/inbox");

app.UseHangfireDashboard("/hangfire", new DashboardOptions
{
    Authorization = [new OwnerOnlyDashboardAuthFilter()],
});

RecurringJob.AddOrUpdate<WebhookLogCleanupJob>(
    "webhook-log-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily);

RecurringJob.AddOrUpdate<MediaRetentionCleanupJob>(
    "media-retention-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily);

RecurringJob.AddOrUpdate<WaveformBackfillJob>(
    "waveform-backfill", job => job.RunAsync(CancellationToken.None), Cron.Daily);

RecurringJob.AddOrUpdate<ConversationAutoReleaseJob>(
    "conversation-auto-release", job => job.RunAsync(CancellationToken.None), Cron.MinuteInterval(15));

// Тозакунии як маротиба (2026-08-21: 52 файли HTML-и канали Instagram) — recurring, вале пас
// аз тозакунии якум ҳамеша холӣ бармегардонад. Дар Hangfire dashboard (/hangfire → Recurring
// Jobs) бо "Trigger now" фавран иҷро кунед, интизори Cron.Daily лозим нест.
RecurringJob.AddOrUpdate<HtmlMediaCleanupJob>(
    "html-media-cleanup", job => job.RunAsync(CancellationToken.None), Cron.Daily);

// Development: ҳамеша иҷро шавад. Production: танҳо агар RUN_MIGRATIONS=true.
var runMigrations = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("RUN_MIGRATIONS");
if (runMigrations)
{
    await app.ApplyMigrationsAsync();
}

app.Run();
