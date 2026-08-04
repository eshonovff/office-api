# Фазаи 0 — Setup

**Ҳадаф:** проекти кориро бо база, log ва ҳуҷҷат ба по мондан.
**Пешшарт:** нест.
**Тахмин:** 1 рӯз.

## Дарун
Скелети проект, пайвасти PostgreSQL, Serilog, Scalar, health check, Docker Compose.

## Берун
Entity, endpoint-и корӣ, auth — ҳеҷ кадоме дар ин фаза нест.

## Вазифаҳо

- [ ] 0.1 Repo `office-api`, `.gitignore` (dotnet), ветка `main` + `dev`
- [ ] 0.2 `dotnet new web -n Office.Api` бо `net10.0`, `Nullable=enable`, `TreatWarningsAsErrors=true`
- [ ] 0.3 Package-ҳо аз `docs/01-architecture.md` (ғайр аз Hangfire)
- [ ] 0.4 `docker-compose.yml` — PostgreSQL 16 + volume
- [ ] 0.5 `AppDbContext` холӣ + пайваст ба Npgsql
- [ ] 0.6 Serilog: консол + файли ротатсияшаванда
- [ ] 0.7 `UseExceptionHandler` + `ProblemDetails` глобалӣ
- [ ] 0.8 OpenAPI + Scalar дар `/scalar` (танҳо Development)
- [ ] 0.9 Health check `/health` (бо санҷиши база)
- [ ] 0.10 User Secrets: `ConnectionStrings:Default`, `Jwt:Key`, `Seed:OwnerPassword`
- [ ] 0.11 `appsettings.json` бе ягон секрет
- [ ] 0.12 CORS policy `Frontend` — `http://localhost:3000` ва `https://office.nizom.tj`

## Файлҳо
`Office.Api.csproj` · `Program.cs` · `appsettings.json` · `Data/AppDbContext.cs` · `docker-compose.yml` · `.gitignore`

## Definition of Done
- `docker compose up -d && dotnet run` бе хатогӣ кор мекунад
- `GET /health` → `200 Healthy`
- `/scalar` кушода мешавад
- `git grep -i "password\|secret" -- appsettings.json` холӣ
