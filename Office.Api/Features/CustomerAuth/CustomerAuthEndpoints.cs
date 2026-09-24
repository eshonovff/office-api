using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Email;

namespace Office.Api.Features.CustomerAuth;

/// <summary>
/// Сабти худии мизоз (email+parol, Google, Apple) — комилан ҷудо аз /api/auth-и кормандон:
/// entity-и худ (Customer), JWT scheme-и худ ("Customer", ниг. Program.cs), cookie-и худ.
/// Дастрасӣ бе RequirePermission — мизоз ҳеҷ гоҳ роль надорад.
/// </summary>
public static class CustomerAuthEndpoints
{
    private static readonly TimeSpan ResendCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan CodeExpiry = TimeSpan.FromMinutes(15);

    public static IEndpointRouteBuilder MapCustomerAuthEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        var group = app.MapGroup("/api/public/auth").WithTags("CustomerAuth");

        group.MapPost("/register", RegisterAsync)
            .WithValidation<RegisterCustomerRequest>()
            .RequireRateLimiting("customer-auth")
            .WithSummary("Сабти ном бо email+parol — коди тасдиқи 6-рақама ба email фиристода мешавад")
            .Produces<CustomerAuthMessageResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/verify-email", VerifyEmailAsync)
            .WithValidation<VerifyCustomerEmailRequest>()
            .RequireRateLimiting("customer-auth")
            .WithSummary("Тасдиқи email бо коди 6-рақама — муваффақ бошад, токен ва refresh cookie медиҳад")
            .Produces<CustomerAuthResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("/resend-code", ResendCodeAsync)
            .WithValidation<ResendCustomerVerificationCodeRequest>()
            .RequireRateLimiting("customer-auth")
            .WithSummary("Фиристодани коди нав — на зудтар аз 60 сония пас аз охирин фиристодан")
            .Produces<CustomerAuthMessageResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/login", LoginAsync)
            .WithValidation<CustomerLoginRequest>()
            .RequireRateLimiting("customer-auth")
            .WithSummary("Воридшавӣ бо email+parol — то email тасдиқ нашавад, рад мешавад")
            .Produces<CustomerAuthResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapPost("/refresh", RefreshAsync)
            .WithSummary("Бо refresh cookie токени нав гирифтан (бо ротатсия)")
            .Produces(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithSummary("Баромадан — refresh token-и ҷорӣ revoke мешавад")
            .Produces(StatusCodes.Status204NoContent);

        group.MapGet("/me", MeAsync)
            .RequireAuthorization(AuthSchemes.CustomerOnlyPolicy)
            .WithSummary("Профили мизози ҷорӣ")
            .Produces<CustomerMeResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        // Танҳо вақте сабт мешаванд, ки Client ID-и провайдер конфигуратсия шуда бошад —
        // бе он схемаи JWT bearer-и дахлдор (Program.cs) ҳам сабт намешавад, пас endpoint
        // санҷиши бесамар намекунад, танҳо намерасад (404), то соҳиби нокомили конфигуратсия
        // тамоми app-ро аз кор наандозад.
        if (!string.IsNullOrEmpty(configuration["Google:ClientId"]))
        {
            group.MapPost("/google", GoogleLoginAsync)
                .RequireAuthorization(policy => policy.AddAuthenticationSchemes(AuthSchemes.Google).RequireAuthenticatedUser())
                .WithSummary("Вуруд/сабти ном тавассути Google — Authorization: Bearer <Google ID token>")
                .Produces<CustomerAuthResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }

        if (!string.IsNullOrEmpty(configuration["Apple:ClientId"]))
        {
            group.MapPost("/apple", AppleLoginAsync)
                .RequireAuthorization(policy => policy.AddAuthenticationSchemes(AuthSchemes.Apple).RequireAuthenticatedUser())
                .WithSummary("Вуруд/сабти ном тавассути Apple — Authorization: Bearer <Apple ID token>")
                .Produces<CustomerAuthResponse>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status401Unauthorized)
                .ProducesProblem(StatusCodes.Status409Conflict);
        }

        return app;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterCustomerRequest request, AppDbContext db, IEmailSender emailSender, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var existing = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);

        if (existing is not null && existing.EmailVerifiedAt is not null)
        {
            return Results.Problem(
                title: "Email аллакай сабт шудааст",
                detail: "Ин email аллакай ба ҳисоби тасдиқшуда тааллуқ дорад.",
                statusCode: StatusCodes.Status409Conflict);
        }

        Customer customer;
        if (existing is not null)
        {
            // Сабти қаблӣ тасдиқ нашуда монда буд (мас. корбар кодро гум кард) — иваз мекунем
            // ва коди нав мефиристем, на 409, то ҳисоби "мурда" боқӣ намонад.
            if (IsResendCoolingDown(existing, DateTimeOffset.UtcNow))
                return TooManyRequestsProblem();

            customer = existing;
            customer.FullName = request.FullName;
            customer.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);
        }
        else
        {
            customer = new Customer
            {
                Id = Guid.CreateVersion7(),
                Email = email,
                FullName = request.FullName,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.Customers.Add(customer);
        }

        await SendVerificationCodeAsync(db, emailSender, customer, ct);

        return Results.Ok(new CustomerAuthMessageResponse("Коди тасдиқ ба email фиристода шуд."));
    }

    private static async Task<IResult> VerifyEmailAsync(
        VerifyCustomerEmailRequest request,
        HttpContext context,
        AppDbContext db,
        ICustomerTokenService tokenService,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (customer is null)
            return InvalidCodeProblem();

        var result = EmailVerificationChecker.Check(
            alreadyVerified: customer.EmailVerifiedAt is not null,
            storedCodeHash: customer.EmailVerificationCodeHash,
            expiresAt: customer.EmailVerificationCodeExpiresAt,
            attempts: customer.EmailVerificationAttempts,
            now: DateTimeOffset.UtcNow,
            submittedCodeHash: HashCode(request.Code));

        if (result == EmailVerificationResult.CodeMismatch)
        {
            customer.EmailVerificationAttempts++;
            await db.SaveChangesAsync(ct);
            return InvalidCodeProblem();
        }

        if (result is EmailVerificationResult.Expired or EmailVerificationResult.TooManyAttempts or EmailVerificationResult.NoCodeRequested)
            return InvalidCodeProblem();

        if (result == EmailVerificationResult.Ok)
        {
            customer.EmailVerifiedAt = DateTimeOffset.UtcNow;
            customer.EmailVerificationCodeHash = null;
            customer.EmailVerificationCodeExpiresAt = null;
            customer.EmailVerificationAttempts = 0;
            await db.SaveChangesAsync(ct);
        }

        // Ok ё AlreadyVerified (дубора зер кардани "тасдиқ" пас аз муваффақият) — ҳарду вуруд медиҳанд.
        var accessToken = await CustomerAuthTokenIssuer.IssueAsync(context, db, tokenService, customer, ct);
        return Results.Ok(new CustomerAuthResponse(accessToken, CustomerMeResponse.From(customer)));
    }

    private static async Task<IResult> ResendCodeAsync(
        ResendCustomerVerificationCodeRequest request, AppDbContext db, IEmailSender emailSender, CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);

        // Мавҷуд набудани email-ро фош намекунем — ҳамон паёми муваффақ бармегардад.
        if (customer is null || customer.EmailVerifiedAt is not null)
            return Results.Ok(new CustomerAuthMessageResponse("Агар ин email сабти нотасдиқ дошта бошад, коди нав фиристода шуд."));

        if (IsResendCoolingDown(customer, DateTimeOffset.UtcNow))
            return TooManyRequestsProblem();

        await SendVerificationCodeAsync(db, emailSender, customer, ct);
        return Results.Ok(new CustomerAuthMessageResponse("Коди нав фиристода шуд."));
    }

    private static async Task<IResult> LoginAsync(
        CustomerLoginRequest request,
        HttpContext context,
        AppDbContext db,
        ICustomerTokenService tokenService,
        CancellationToken ct)
    {
        var email = NormalizeEmail(request.Email);
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Email == email, ct);

        if (customer is null || customer.PasswordHash is null || !customer.IsActive ||
            !BCrypt.Net.BCrypt.Verify(request.Password, customer.PasswordHash))
        {
            return Results.Problem(
                title: "Хатогии воридшавӣ",
                detail: "Email ё parol нодуруст.",
                statusCode: StatusCodes.Status401Unauthorized);
        }

        if (customer.EmailVerifiedAt is null)
        {
            return Results.Problem(
                title: "Email тасдиқ нашудааст",
                detail: "Пеш аз воридшавӣ email-и худро тасдиқ кунед.",
                statusCode: StatusCodes.Status403Forbidden);
        }

        customer.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var accessToken = await CustomerAuthTokenIssuer.IssueAsync(context, db, tokenService, customer, ct);
        return Results.Ok(new CustomerAuthResponse(accessToken, CustomerMeResponse.From(customer)));
    }

    private static async Task<IResult> RefreshAsync(
        HttpContext context, AppDbContext db, ICustomerTokenService tokenService, CancellationToken ct)
    {
        if (!context.Request.Cookies.TryGetValue(CustomerAuthTokenIssuer.RefreshCookieName, out var plainToken) ||
            string.IsNullOrEmpty(plainToken))
        {
            return Results.Unauthorized();
        }

        var hash = tokenService.HashRefreshToken(plainToken);
        var stored = await db.CustomerRefreshTokens
            .Include(rt => rt.Customer)
            .FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);

        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt < DateTimeOffset.UtcNow || !stored.Customer.IsActive)
            return Results.Unauthorized();

        stored.RevokedAt = DateTimeOffset.UtcNow;

        var accessToken = await CustomerAuthTokenIssuer.IssueAsync(context, db, tokenService, stored.Customer, ct);
        return Results.Ok(new { accessToken });
    }

    private static async Task<IResult> LogoutAsync(
        HttpContext context, AppDbContext db, ICustomerTokenService tokenService, CancellationToken ct)
    {
        if (context.Request.Cookies.TryGetValue(CustomerAuthTokenIssuer.RefreshCookieName, out var plainToken) &&
            !string.IsNullOrEmpty(plainToken))
        {
            var hash = tokenService.HashRefreshToken(plainToken);
            var stored = await db.CustomerRefreshTokens.FirstOrDefaultAsync(rt => rt.TokenHash == hash, ct);
            if (stored is not null && stored.RevokedAt is null)
            {
                stored.RevokedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);
            }
        }

        context.Response.Cookies.Delete(CustomerAuthTokenIssuer.RefreshCookieName);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(ClaimsPrincipal principal, AppDbContext db, CancellationToken ct)
    {
        var customerId = principal.GetUserId();
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct);
        return customer is null ? Results.Unauthorized() : Results.Ok(CustomerMeResponse.From(customer));
    }

    private static Task<IResult> GoogleLoginAsync(
        ClaimsPrincipal principal, HttpContext context, AppDbContext db, ICustomerTokenService tokenService, CancellationToken ct) =>
        ExternalLoginAsync(CustomerExternalLoginProvider.Google, principal, context, db, tokenService, ct);

    private static Task<IResult> AppleLoginAsync(
        ClaimsPrincipal principal, HttpContext context, AppDbContext db, ICustomerTokenService tokenService, CancellationToken ct) =>
        ExternalLoginAsync(CustomerExternalLoginProvider.Apple, principal, context, db, tokenService, ct);

    /// <summary>
    /// principal аллакай тасдиқшуда аст — ASP.NET Core худи ID token-и Google/Apple-ро аз
    /// рӯи JWKS-и он провайдер тасдиқ кардааст (Program.cs, Authority-based). Ин ҷо танҳо
    /// қарори "кадом Customer" мемонад (ExternalLoginResolver, pure, тест шудааст).
    /// </summary>
    private static async Task<IResult> ExternalLoginAsync(
        CustomerExternalLoginProvider provider,
        ClaimsPrincipal principal,
        HttpContext context,
        AppDbContext db,
        ICustomerTokenService tokenService,
        CancellationToken ct)
    {
        var providerUserId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (string.IsNullOrEmpty(providerUserId))
            return Results.Unauthorized();

        var email = principal.FindFirst("email")?.Value;
        var emailVerifiedClaim = principal.FindFirst("email_verified")?.Value;
        // Apple (ва баъзан Google) "email_verified"-ро ҳамчун сатр мефиристад, на bool JSON.
        var emailVerified = emailVerifiedClaim is "true" or "1";
        var fullName = principal.FindFirst("name")?.Value;

        var linkedCustomer = await db.Customers
            .FirstOrDefaultAsync(c => c.ExternalLogins.Any(l => l.Provider == provider && l.ProviderUserId == providerUserId), ct);

        if (linkedCustomer is null && string.IsNullOrEmpty(email))
        {
            // Apple email-ро танҳо дар аввалин авторизатсия мефиристад — агар пайванд
            // набошад ва email ҳам набошад, ин корбарро муайян карда наметавонем.
            return Results.Problem(
                title: "Email лозим аст",
                detail: "Провайдер email нафиристод. Лутфан бо email+parol сабти ном кунед.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var normalizedEmail = email is null ? null : NormalizeEmail(email);
        var customerByEmail = linkedCustomer is null && normalizedEmail is not null
            ? await db.Customers.FirstOrDefaultAsync(c => c.Email == normalizedEmail, ct)
            : null;

        var action = ExternalLoginResolver.Resolve(linkedCustomer is not null, customerByEmail is not null, emailVerified);

        Customer customer;
        switch (action)
        {
            case ExternalLoginAction.UseLinkedCustomer:
                customer = linkedCustomer!;
                break;

            case ExternalLoginAction.LinkToExistingCustomerByEmail:
                // Ин шоха танҳо вақте мерасад, ки providerEmailVerified=true (ExternalLoginResolver) —
                // пас агар customer аз сабти қаблии email+parol нотасдиқ монда бошад (масалан корбар
                // коди тасдиқро гум карда буд), Google/Apple аллакай онро тасдиқ карда — набояд
                // EmailVerifiedAt-ро null монем, вагарна POST /login (email+parol) баъдтар 403 медиҳад.
                customer = customerByEmail!;
                customer.EmailVerifiedAt ??= DateTimeOffset.UtcNow;
                db.CustomerExternalLogins.Add(NewExternalLogin(customer.Id, provider, providerUserId));
                break;

            case ExternalLoginAction.CreateNewCustomer:
                customer = new Customer
                {
                    Id = Guid.CreateVersion7(),
                    Email = normalizedEmail!,
                    FullName = string.IsNullOrWhiteSpace(fullName) ? normalizedEmail! : fullName,
                    PasswordHash = null,
                    EmailVerifiedAt = DateTimeOffset.UtcNow,
                    IsActive = true,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                db.Customers.Add(customer);
                db.CustomerExternalLogins.Add(NewExternalLogin(customer.Id, provider, providerUserId));
                break;

            case ExternalLoginAction.RejectUnverifiedEmail:
            default:
                return Results.Problem(
                    title: "Email тасдиқ нашудааст",
                    detail: "Ин email аллакай ба ҳисоби дигар тааллуқ дорад, вале провайдер онро тасдиқшуда надонист.",
                    statusCode: StatusCodes.Status409Conflict);
        }

        customer.LastLoginAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        var accessToken = await CustomerAuthTokenIssuer.IssueAsync(context, db, tokenService, customer, ct);
        return Results.Ok(new CustomerAuthResponse(accessToken, CustomerMeResponse.From(customer)));
    }

    private static CustomerExternalLogin NewExternalLogin(Guid customerId, CustomerExternalLoginProvider provider, string providerUserId) => new()
    {
        Id = Guid.CreateVersion7(),
        CustomerId = customerId,
        Provider = provider,
        ProviderUserId = providerUserId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static bool IsResendCoolingDown(Customer customer, DateTimeOffset now) =>
        customer.EmailVerificationSentAt is not null && now - customer.EmailVerificationSentAt < ResendCooldown;

    private static async Task SendVerificationCodeAsync(AppDbContext db, IEmailSender emailSender, Customer customer, CancellationToken ct)
    {
        var code = PasswordGenerator.GenerateNumeric(6);
        var now = DateTimeOffset.UtcNow;

        customer.EmailVerificationCodeHash = HashCode(code);
        customer.EmailVerificationCodeExpiresAt = now.Add(CodeExpiry);
        customer.EmailVerificationAttempts = 0;
        customer.EmailVerificationSentAt = now;

        await db.SaveChangesAsync(ct);

        await emailSender.SendAsync(
            customer.Email,
            "Коди тасдиқи email — office.nizom.tj",
            $"Салом, {customer.FullName}!\n\nКоди тасдиқи шумо: {code}\n\nИн код 15 дақиқа амал мекунад. " +
            "Агар шумо ин дархостро накарда бошед, ин email-ро нодида гиред.",
            ct);
    }

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    private static IResult InvalidCodeProblem() => Results.Problem(
        title: "Коди нодуруст",
        detail: "Коди ворид кардашуда нодуруст, кӯҳна ё аллакай истифодашуда аст.",
        statusCode: StatusCodes.Status400BadRequest);

    private static IResult TooManyRequestsProblem() => Results.Problem(
        title: "Аз ҳад зиёд дархост",
        detail: "Лутфан пеш аз дархости нав каме интизор шавед.",
        statusCode: StatusCodes.Status429TooManyRequests);
}
