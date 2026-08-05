using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Features.Auth;
using Office.Api.Features.Projects;
using Office.Api.Features.Roles;
using Office.Api.Features.Tasks;
using Office.Api.Features.Users;
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

builder.Services.AddOpenApi();

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("postgres");

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy => policy
        .WithOrigins("http://localhost:3000", "https://office.nizom.tj")
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

builder.Services.AddScoped<IPermissionService, PermissionService>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<ProjectAccessGuard>();

var app = builder.Build();

app.UseExceptionHandler(exceptionHandlerApp => exceptionHandlerApp.Run(async context =>
{
    var problemDetailsService = context.RequestServices.GetRequiredService<IProblemDetailsService>();
    await problemDetailsService.WriteAsync(new ProblemDetailsContext
    {
        HttpContext = context,
        ProblemDetails =
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
    app.MapScalarApiReference();
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

// Development: ҳамеша иҷро шавад. Production: танҳо агар RUN_MIGRATIONS=true.
var runMigrations = app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("RUN_MIGRATIONS");
if (runMigrations)
{
    await app.ApplyMigrationsAsync();
}

app.Run();
