# 01 — Архитектура

## Сохтори папкаҳо

```
office-api/
  Office.Api/
    Program.cs
    appsettings.json
    Office.Api.csproj

    Auth/                    # авторизатсия ва токен
      Permissions.cs
      PermissionAuthorization.cs
      TokenService.cs

    Data/                    # EF Core
      AppDbContext.cs
      Entities/
      Configurations/
      Migrations/
      DbSeeder.cs

    Features/                # як папка = як домен
      Auth/
      Users/
      Roles/
      Projects/
      Tasks/
      Channels/
      Conversations/
      Templates/

    Channels/                # интеграцияҳои беруна
      IChannelProvider.cs
      WhatsApp/
      Messenger/
      Instagram/

    Realtime/                # SignalR
    Common/                  # натиҷаҳо, exception handler, extension
```

## Қоидаҳои архитектурӣ

1. **Vertical slice.** Ҳар feature дар папкаи худаш: endpoint, request, response, validator, handler. Қабатҳои `Services/`, `Repositories/`-и умумӣ насоз.
2. **Minimal APIs,** на Controller. Ҳар феча як `static class ...Endpoints` бо методи `Map...Endpoints(this IEndpointRouteBuilder)`.
3. **DbContext мустақим дар endpoint** — repository pattern лозим нест, EF Core худаш repository аст.
4. **DTO ҳатмӣ.** Entity ҳеҷ гоҳ аз API берун намеравад.
5. **`record` барои DTO,** `class` барои entity.
6. **CancellationToken** дар ҳар методи async.
7. Логикаи такроршаванда (ҳисоби доступ, position, тиреза) → сервиси алоҳида дар `Common/` ё `Auth/`.

## Package-ҳои иҷозатдодашуда

```
Npgsql.EntityFrameworkCore.PostgreSQL     10.x
Microsoft.EntityFrameworkCore.Design      10.x
Microsoft.AspNetCore.Authentication.JwtBearer  10.x
Microsoft.AspNetCore.OpenApi              10.x
Scalar.AspNetCore                         2.x
Serilog.AspNetCore                        9.x
BCrypt.Net-Next                           4.x
FluentValidation                          12.x
Hangfire.AspNetCore + Hangfire.PostgreSql (фазаи 4)
```

Package-и дигар — аввал пурс. **AutoMapper ва MediatR лозим нест.**

## Сарҳадҳо

- База танҳо аз `Office.Api` дастрас — NIZOM CRM ба ин база даст намерасонад
- Webhook-и Meta бояд аз интернет дастрас бошад → Cloudflare proxy хомӯш барои `/webhooks/*`
- Файлҳо: локалӣ дар `/var/office/uploads`, дертар MinIO
