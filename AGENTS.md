# AGENTS.md — office.nizom.tj (Backend)

> Ин файлро **ҳамеша аввал бихон**. Баъд файли фазаи ҷорӣ.

## Проект чист

Платформаи дохилии ширкати SMARTWEB TJ (соҳиби NIZOM CRM).
**Барои худамон**, на барои мижозон. Домен: `office.nizom.tj`

Чор вазифаи асосӣ:
1. Кормандон + доступи дақиқ (кӣ кадом саҳифа ва функсияро мебинад)
2. Проект ва таск бо drag & drop
3. Пайвасти Instagram, Facebook, WhatsApp
4. Инбокси умумӣ — чатҳо бо статус, таъин ба корманд бо drag & drop

## Ин дар проект НЕСТ

Инҳоро **насоз**, ҳатто агар фоиданок ба назар расанд:
- Молия, ҳисоби маош, HR, ҳозирӣ
- Telegram (қасдан рад шудааст)
- Multi-tenancy — ин як ширкат аст
- Ҳисоботи мураккаб, BI, dashboard-и аналитикӣ
- Мобилӣ (баъдтар, ҳозир не)

Агар вазифае берун аз ин ҳудуд ба назарат зарур намояд — **иҷро накун**, дар PR ёддошт навису пурс.

## Стек

| Қабат | Технология |
|---|---|
| Runtime | .NET 10 (LTS) / C# 14 |
| API | ASP.NET Core Minimal APIs |
| ORM | EF Core 10 |
| База | PostgreSQL 16+ |
| Auth | JWT (access 15 дақ) + refresh cookie (30 рӯз) |
| Realtime | SignalR |
| Background | Hangfire |
| Docs | OpenAPI + Scalar (`/scalar`) |
| Log | Serilog |
| Hash | BCrypt.Net-Next |

**.NET 8/9 нагир** — 10 ноябри 2026 дастгириро гум мекунанд.

## Файлҳои ҳуҷҷат

| Файл | Кай хонӣ |
|---|---|
| `AGENTS.md` | ҳамеша |
| `docs/PROGRESS.md` | ҳамеша — ҳолати ҷорӣ |
| `docs/01-architecture.md` | пеш аз сохтани файли нав |
| `docs/02-conventions.md` | пеш аз навиштани код |
| `docs/03-permissions.md` | ҳар кор бо доступ |
| `docs/04-data-model.md` | ҳар кор бо база |
| `docs/phases/phase-N-*.md` | вазифаи ҷорӣ |

## Қоидаҳои кор

1. **Як фаза — як ветка.** `feat/phase-1-auth`
2. Пеш аз сар кардан, `docs/PROGRESS.md`-ро хон — фазаи ҷорӣ кадом аст.
3. Танҳо вазифаҳои файли фазаи ҷориро иҷро кун. Ба фазаи оянда нагузар.
4. Баъди ҳар вазифа checkbox-ро дар файли фаза `[x]` кун.
5. Ҳар фаза **Definition of Done** дорад — то он иҷро нашавад, фаза тамом нест.
6. Ҳар тағйири модел бояд migration дошта бошад. Migration худаш ҳангоми старт иҷро мешавад — дастӣ database update лозим нест.
7. Package-и нав илова накун агар дар `01-architecture.md` набошад — аввал пурс.
8. Секрет (парол, токен, калид) ҳеҷ гоҳ дар код ё `appsettings.json` — танҳо User Secrets / env.

## Тартиби пӯшидани фаза

Баъди тамом шудани ҳар фаза, агент ин тартибро иҷро мекунад —
бе фармони алоҳида, вале ТАНҲО агар ҳар се шарт иҷро шуда бошад:

1. `dotnet build` бе warning ва error
2. `dotnet test` — ҳама сабз
3. Definition of Done-и файли фаза банд ба банд тасдиқ шуда,
   бо далели воқеӣ (натиҷаи команда, на даъво)

Агар ҳатто як шарт иҷро нашуда бошад — merge ва push НАКУН,
масъаларо ба корбар нишон деҳ ва интизор шав.

Тартиб:

1. Ҳамаи checkbox-ҳои файли фаза `[x]` шуда бошанд
2. `docs/PROGRESS.md` нав шавад: фаза = ✅, фазаи ҷорӣ = фазаи оянда,
   сана гузошта шавад. Қарорҳои нав ба ҷадвали "Қарорҳои қабулшуда"
3. Commit дар ветка феча
4. `git checkout dev && git merge --no-ff feat/phase-N-name`
   Хабари merge: `merge: phase N — <ном>`
5. Аз `dev` тоза бисанҷ:
   `docker compose down -v && docker compose up -d && dotnet run`
   — migration ва seed бе хато гузаранд
   `dotnet test` — сабз
6. `git checkout main && git merge --no-ff dev`
7. Tag: `git tag -a phase-N -m "Phase N: <ном>"`
8. `git push origin main dev --tags`
9. Ветка феча: локалӣ ва remote нест кун
10. Ба корбар хулоса деҳ: чӣ сохта шуд, кадом commit-ҳо,
    линки repo, тег

Conflict шавад — худат ҳал накун, ба корбар нишон деҳ.

## Забон

- Код, ном, комментарийи техникӣ — **англисӣ**
- Матни хатогӣ ба корбар — **тоҷикӣ**
- Commit message — англисӣ, Conventional Commits
