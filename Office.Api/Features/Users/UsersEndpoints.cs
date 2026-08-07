using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Office.Api.Auth;
using Office.Api.Common;
using Office.Api.Data;
using Office.Api.Data.Entities;
using Office.Api.Sms;
using Permissions = Office.Api.Auth.Permissions;

namespace Office.Api.Features.Users;

public static class UsersEndpoints
{
    public static IEndpointRouteBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/users").WithTags("Users");

        group.MapGet("/", ListAsync)
            .RequirePermission(Permissions.Users.View)
            .WithSummary("Рӯйхати корманд — филтр бо ном/логин, роль, фаъол будан")
            .Produces<IEnumerable<UserListItem>>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetAsync)
            .RequirePermission(Permissions.Users.View)
            .WithSummary("Маълумоти пурраи корманд — роль ва истиснои permission")
            .Produces<UserDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/", CreateAsync)
            .DisableAntiforgery()
            .RequirePermission(Permissions.Users.Manage)
            .WithSummary("Сохтани корманди нав бо пароли муваққатӣ — multipart/form-data, расм ихтиёрӣ")
            .Produces<CreateUserResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPatch("/{id:guid}", UpdateAsync)
            .WithValidation<UpdateUserRequest>()
            .RequirePermission(Permissions.Users.Manage)
            .WithSummary("Навсозии ном, телефон, only_assigned")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/roles", SetRolesAsync)
            .WithValidation<SetUserRolesRequest>()
            .RequirePermission(Permissions.Users.Manage)
            .WithSummary("Иваз кардани ролҳои корманд — permissions_version боло меравад")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}/permissions", SetPermissionsAsync)
            .WithValidation<SetUserPermissionsRequest>()
            .RequirePermission(Permissions.Users.Manage)
            .WithSummary("Танзими истиснои permission-и шахсӣ — permissions_version боло меравад")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/reset-password", ResetPasswordAsync)
            .RequirePermission(Permissions.Users.Manage)
            .WithSummary("Пароли муваққатии нав сохтан")
            .Produces<ResetPasswordResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("/{id:guid}/active", SetActiveAsync)
            .RequirePermission(Permissions.Users.Manage)
            .WithSummary("Фаъол/ғайрифаъол кардани корманд (soft disable)")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/avatar", UploadAvatarAsync)
            .RequirePermission(Permissions.Users.Manage)
            .DisableAntiforgery()
            .WithSummary("Бор кардани расми корманд — файли қаблӣ иваз мешавад")
            .Produces<UserDetail>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/avatar", DownloadAvatarAsync)
            .RequirePermission(Permissions.Users.View)
            .WithSummary("Боргирии расми корманд")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/contract-document", UploadContractDocumentAsync)
            .RequirePermission(Permissions.Users.Manage)
            .DisableAntiforgery()
            .WithSummary("Бор кардани ҳуҷҷати шартнома — файли қаблӣ иваз мешавад")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/{id:guid}/contract-document", DownloadContractDocumentAsync)
            .RequirePermission(Permissions.Users.Manage)
            .WithSummary("Боргирии ҳуҷҷати шартнома")
            .Produces(StatusCodes.Status200OK, contentType: "application/octet-stream")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static readonly HashSet<string> AllowedDocumentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".jpg", ".jpeg", ".png",
    };

    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp",
    };

    private static async Task<IResult> ListAsync(
        AppDbContext db,
        string? search,
        Guid? roleId,
        bool? isActive,
        CancellationToken ct)
    {
        var query = db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.FullName, pattern) || EF.Functions.ILike(u.Username, pattern));
        }

        if (roleId is not null)
            query = query.Where(u => u.UserRoles.Any(ur => ur.RoleId == roleId));

        if (isActive is not null)
            query = query.Where(u => u.IsActive == isActive);

        var users = await query.OrderBy(u => u.FullName).ToListAsync(ct);
        return Results.Ok(users.Select(ToListItem));
    }

    private static async Task<IResult> GetAsync(Guid id, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == id, ct);

        return user is null ? Results.NotFound() : Results.Ok(ToDetail(user));
    }

    private static async Task<IResult> CreateAsync(
        [FromForm] string fullName,
        [FromForm] string phone,
        [FromForm] string? email,
        [FromForm] DateOnly? birthDate,
        [FromForm] string? address,
        [FromForm] string? gender,
        IFormFile? avatar,
        AppDbContext db,
        IValidator<CreateUserRequest> validator,
        ISmsSender smsSender,
        IConfiguration configuration,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var request = new CreateUserRequest(fullName, phone, email, birthDate, address, gender);
        var validationResult = await validator.ValidateAsync(request, ct);
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
            return Results.ValidationProblem(errors);
        }

        var normalizedPhone = PhoneNumber.Normalize(phone)!;

        var phoneTaken = await db.Users.AnyAsync(u => u.Username == normalizedPhone, ct);
        if (phoneTaken)
        {
            return Results.Problem(
                title: "Рақами телефон банд аст",
                detail: "Корманд бо ин рақами телефон аллакай сабт шудааст.",
                statusCode: StatusCodes.Status409Conflict);
        }

        var temporaryPassword = PasswordGenerator.GenerateNumeric();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            FullName = fullName,
            Username = normalizedPhone,
            Phone = normalizedPhone,
            Email = email,
            BirthDate = birthDate,
            Address = address,
            Gender = string.IsNullOrEmpty(gender) ? null : Enum.Parse<Gender>(gender, ignoreCase: true),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword),
            IsActive = true,
            MustChangePassword = true,
            PermissionsVersion = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (avatar is not null)
        {
            var avatarError = await SaveAvatarAsync(user, avatar, configuration, env, ct);
            if (avatarError is not null)
                return avatarError;
        }

        db.Users.Add(user);
        await db.SaveChangesAsync(ct);

        var smsSent = await smsSender.SendAsync(
            normalizedPhone, $"office.nizom.tj\nЛогин: {normalizedPhone}\nПарол: {temporaryPassword}", ct);

        return Results.Created(
            $"/api/users/{user.Id}",
            new CreateUserResponse(user.Id, user.Username, temporaryPassword, smsSent, user.AvatarUrl));
    }

    private static async Task<IResult> UpdateAsync(Guid id, UpdateUserRequest request, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        user.FullName = request.FullName;
        user.Email = request.Email;
        user.BirthDate = request.BirthDate;
        user.Address = request.Address;
        user.Gender = string.IsNullOrEmpty(request.Gender) ? null : Enum.Parse<Gender>(request.Gender, ignoreCase: true);
        user.OnlyAssigned = request.OnlyAssigned;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> SetRolesAsync(
        Guid id,
        SetUserRolesRequest request,
        AppDbContext db,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserRoles).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        var roleIds = request.RoleIds.Distinct().ToList();
        var existingRoleCount = await db.Roles.CountAsync(r => roleIds.Contains(r.Id), ct);
        if (existingRoleCount != roleIds.Count)
        {
            return Results.Problem(
                title: "Роли нодуруст",
                detail: "Яке аз роль-ҳо вуҷуд надорад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        user.UserRoles.Clear();
        foreach (var roleId in roleIds)
            user.UserRoles.Add(new UserRole { UserId = user.Id, RoleId = roleId });

        await db.SaveChangesAsync(ct);
        await permissionService.BumpVersionAsync(user.Id, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> SetPermissionsAsync(
        Guid id,
        SetUserPermissionsRequest request,
        AppDbContext db,
        IPermissionService permissionService,
        CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserPermissions).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        user.UserPermissions.Clear();
        foreach (var exception in request.Exceptions)
        {
            user.UserPermissions.Add(new UserPermission
            {
                UserId = user.Id,
                PermissionKey = exception.PermissionKey,
                IsGranted = exception.IsGranted,
            });
        }

        await db.SaveChangesAsync(ct);
        await permissionService.BumpVersionAsync(user.Id, ct);

        return Results.NoContent();
    }

    private static async Task<IResult> ResetPasswordAsync(Guid id, AppDbContext db, ISmsSender smsSender, CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        var temporaryPassword = PasswordGenerator.GenerateNumeric();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(temporaryPassword);
        user.MustChangePassword = true;

        await db.SaveChangesAsync(ct);

        var smsSent = user.Phone is null
            ? false
            : await smsSender.SendAsync(user.Phone, $"office.nizom.tj\nПарол: {temporaryPassword}", ct);

        return Results.Ok(new ResetPasswordResponse(temporaryPassword, smsSent));
    }

    private static async Task<IResult> UploadAvatarAsync(
        Guid id,
        IFormFile file,
        AppDbContext db,
        IConfiguration configuration,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var user = await db.Users
            .Include(u => u.UserRoles).ThenInclude(ur => ur.Role)
            .Include(u => u.UserPermissions)
            .FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        var error = await SaveAvatarAsync(user, file, configuration, env, ct);
        if (error is not null)
            return error;

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDetail(user));
    }

    /// <summary>Санҷиш ва захираи файли расм дар диск; майдонҳои Avatar*-и user-ро дар ҳофиза иваз мекунад.</summary>
    private static async Task<IResult?> SaveAvatarAsync(
        User user, IFormFile file, IConfiguration configuration, IWebHostEnvironment env, CancellationToken ct)
    {
        if (file.Length <= 0)
            return Results.BadRequest();

        var maxSizeBytes = configuration.GetValue<long?>("Uploads:MaxSizeBytes") ?? 20 * 1024 * 1024;
        if (file.Length > maxSizeBytes)
        {
            return Results.Problem(
                title: "Файл калон аст",
                detail: $"Ҳаҷми файл набояд аз {maxSizeBytes / (1024 * 1024)} МБ зиёд бошад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedAvatarExtensions.Contains(extension))
        {
            return Results.Problem(
                title: "Навъи файл иҷозат дода нашудааст",
                detail: "Расм бояд .jpg, .jpeg, .png ё .webp бошад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var rootPath = ResolveRootPath(configuration, env);
        var avatarFolder = Path.Combine(rootPath, "users", user.Id.ToString(), "avatar");
        Directory.CreateDirectory(avatarFolder);

        if (user.AvatarPath is not null)
        {
            var oldFullPath = Path.Combine(rootPath, user.AvatarPath);
            if (File.Exists(oldFullPath))
                File.Delete(oldFullPath);
        }

        var storedFileName = $"{Guid.CreateVersion7()}{extension}";
        var fullPath = Path.Combine(avatarFolder, storedFileName);

        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        user.AvatarPath = Path.Combine("users", user.Id.ToString(), "avatar", storedFileName);
        user.AvatarUrl = $"/api/users/{user.Id}/avatar";

        return null;
    }

    private static async Task<IResult> DownloadAvatarAsync(
        Guid id, AppDbContext db, IConfiguration configuration, IWebHostEnvironment env, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user?.AvatarPath is null)
            return Results.NotFound();

        var rootPath = ResolveRootPath(configuration, env);
        var fullPath = Path.Combine(rootPath, user.AvatarPath);
        if (!File.Exists(fullPath))
            return Results.NotFound();

        return Results.File(fullPath, "application/octet-stream");
    }

    private static async Task<IResult> UploadContractDocumentAsync(
        Guid id,
        IFormFile file,
        AppDbContext db,
        IConfiguration configuration,
        IWebHostEnvironment env,
        CancellationToken ct)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        if (file.Length <= 0)
            return Results.BadRequest();

        var maxSizeBytes = configuration.GetValue<long?>("Uploads:MaxSizeBytes") ?? 20 * 1024 * 1024;
        if (file.Length > maxSizeBytes)
        {
            return Results.Problem(
                title: "Файл калон аст",
                detail: $"Ҳаҷми файл набояд аз {maxSizeBytes / (1024 * 1024)} МБ зиёд бошад.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrEmpty(extension) || !AllowedDocumentExtensions.Contains(extension))
        {
            return Results.Problem(
                title: "Навъи файл иҷозат дода нашудааст",
                detail: "Ин навъи файлро бор кардан мумкин нест.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var rootPath = ResolveRootPath(configuration, env);
        var userFolder = Path.Combine(rootPath, "users", id.ToString());
        Directory.CreateDirectory(userFolder);

        if (user.ContractDocumentPath is not null)
        {
            var oldFullPath = Path.Combine(rootPath, user.ContractDocumentPath);
            if (File.Exists(oldFullPath))
                File.Delete(oldFullPath);
        }

        var storedFileName = $"{Guid.CreateVersion7()}{extension}";
        var fullPath = Path.Combine(userFolder, storedFileName);

        await using (var stream = File.Create(fullPath))
            await file.CopyToAsync(stream, ct);

        user.ContractDocumentPath = Path.Combine("users", id.ToString(), storedFileName);
        user.ContractDocumentFileName = file.FileName;

        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> DownloadContractDocumentAsync(
        Guid id, AppDbContext db, IConfiguration configuration, IWebHostEnvironment env, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user?.ContractDocumentPath is null)
            return Results.NotFound();

        var rootPath = ResolveRootPath(configuration, env);
        var fullPath = Path.Combine(rootPath, user.ContractDocumentPath);
        if (!File.Exists(fullPath))
            return Results.NotFound();

        return Results.File(fullPath, "application/octet-stream", user.ContractDocumentFileName);
    }

    private static string ResolveRootPath(IConfiguration configuration, IWebHostEnvironment env)
    {
        var configured = configuration["Uploads:RootPath"];
        var basePath = configured is { Length: > 0 }
            ? (Path.IsPathRooted(configured) ? configured : Path.Combine(env.ContentRootPath, configured))
            : Path.Combine(env.ContentRootPath, "uploads");

        return Path.GetFullPath(basePath);
    }

    private static async Task<IResult> SetActiveAsync(Guid id, SetActiveRequest request, AppDbContext db, CancellationToken ct)
    {
        var user = await db.Users.Include(u => u.UserRoles).ThenInclude(ur => ur.Role).FirstOrDefaultAsync(u => u.Id == id, ct);
        if (user is null)
            return Results.NotFound();

        if (!request.IsActive && user.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner))
        {
            var activeOwnerCount = await db.Users
                .CountAsync(u => u.IsActive && u.UserRoles.Any(ur => ur.Role.Key == RoleKeys.Owner), ct);

            if (activeOwnerCount <= 1)
            {
                return Results.Problem(
                    title: "Owner-и охирин",
                    detail: "Owner-и охиринро ғайрифаъол кардан мумкин нест.",
                    statusCode: StatusCodes.Status409Conflict);
            }
        }

        user.IsActive = request.IsActive;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static int? CalculateAge(DateOnly? birthDate) =>
        birthDate is null ? null : AgeCalculator.Calculate(birthDate.Value, DateOnly.FromDateTime(DateTime.UtcNow));

    private static UserListItem ToListItem(User user) => new(
        user.Id,
        user.FullName,
        user.Username,
        user.Phone,
        user.Email,
        user.BirthDate,
        CalculateAge(user.BirthDate),
        user.Address,
        user.Gender?.ToString(),
        user.AvatarUrl,
        user.ContractDocumentPath is not null,
        user.IsActive,
        user.MustChangePassword,
        user.UserRoles.Select(ur => new RoleSummary(ur.Role.Id, ur.Role.Key, ur.Role.Name)).ToList());

    private static UserDetail ToDetail(User user) => new(
        user.Id,
        user.FullName,
        user.Username,
        user.Phone,
        user.Email,
        user.BirthDate,
        CalculateAge(user.BirthDate),
        user.Address,
        user.Gender?.ToString(),
        user.ContractDocumentPath is not null,
        user.AvatarUrl,
        user.IsActive,
        user.MustChangePassword,
        user.OnlyAssigned,
        user.UserRoles.Select(ur => new RoleSummary(ur.Role.Id, ur.Role.Key, ur.Role.Name)).ToList(),
        user.UserPermissions.Select(up => new UserPermissionExceptionDto(up.PermissionKey, up.IsGranted)).ToList());
}

public record SetActiveRequest(bool IsActive);
