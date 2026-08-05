# PROGRESS — ҳолати ҷорӣ

> Агент: ин файлро баъди ҳар фаза нав кун.

**Фазаи ҷорӣ:** `phase-3-realtime`
**Ветка:** `feat/phase-3-realtime`
**Санаи навсозӣ:** 2026-08-05

## Ҳолати фазаҳо

| Фаза | Ном | Ҳолат |
|---|---|---|
| 0 | Setup | ✅ тамом |
| 1 | Auth ва доступ | ✅ тамом |
| 2 | Проект ва таск | ✅ тамом |
| 3 | Realtime | ✅ тамом |
| 4 | Инфраструктураи каналҳо | ⬜ нашуда |
| 5 | WhatsApp | ⬜ нашуда |
| 6 | Инбокс | ⬜ нашуда |
| 7 | Instagram + Facebook | ⬜ нашуда |
| 8 | Deploy | ⬜ нашуда |

Ҳолатҳо: ⬜ нашуда · 🟡 дар кор · ✅ тамом · ⛔ басташуда

## Қарорҳои қабулшуда

| Сана | Қарор | Сабаб |
|---|---|---|
| — | Telegram илова намешавад | Мижозон дар IG/FB/WA ҳастанд |
| — | Молия ва маош нест | Ширкат хурд аст |
| — | Як проекти .NET, на Clean Architecture | Ҳаҷм иҷозат медиҳад |
| 2026-08-04 | PostgreSQL-и локалӣ дар портти `5433` (на 5432) | Порти 5432 дар мошин аз ҷониби контейнери дигар (`shop-postgres`, лоиҳаи дигар) банд аст |
| 2026-08-04 | `Microsoft.OpenApi` ба 2.11.0 pin шуд (на 2.0.0) | 2.0.0 (transitive аз `Microsoft.AspNetCore.OpenApi` 10.0.10) NU1903 high-severity vulnerability дошт; 3.x API-и `IOpenApiMediaType.Example`-ро breaking мекунад |
| 2026-08-04 | `JwtBearerOptions.MapInboundClaims = false` | Пешфарз claim-и `sub` ба URI-и дарози `ClaimTypes` ремап мешавад — middleware-и `pv` онро намеёфт, ҳамеша 401 медод |
| 2026-08-04 | Формулаи permission ба `Auth/PermissionResolver.cs` (pure, бе DB) ҷудо шуд | Барои тест 1.10 бе package-и нав (InMemory/Sqlite) кофӣ буд |
| 2026-08-04 | `Office.Api.Tests` (xUnit) ва `Office.Api.slnx` илова шуд | Вазифаи 1.10 тестро ҳатмӣ мекунад; дар `01-architecture.md` package-и тест зикр нашудааст, вале xUnit стандарти dotnet аст |
| 2026-08-04 | `dotnet-ef` global tool аз 9.0.18 ба 10.0.10 нав шуд | Номувофиқии версия бо EF Core 10 apphost-ро вайрон карда буд |
| 2026-08-05 | Кӯчонидани таск: advisory lock (`pg_advisory_xact_lock(hashtext(columnId))`) дар transaction, на raw `FOR UPDATE` | EF Core-ро бо LINQ маҳдуд намекунад ва ду кӯчонидани ҳамзамони як колонкаро serialize мекунад (2.20) |
| 2026-08-05 | `LabelIds` ба `CreateTaskRequest`/`UpdateTaskRequest` илова шуд | `task_labels` дар `docs/04` ҳаст, вале вазифаи алоҳида барои васл кардани тег ба таск номбар нашудааст — табиист онро дар create/update ҷо кунем |
| 2026-08-05 | `Uploads:RootPath` дар `appsettings.Development.json` ба `./uploads` (на `/var/office/uploads`) | Дар mac локалӣ роҳи прод (`/var/office/uploads`) бе root дастрас нест |
| 2026-08-05 | `ProjectAccessGuard` бо interface `IProjectAccessGuard` кушода шуд | Барои тест 3.4 (BoardHub) бе package-и mocking — fake дастӣ дар DI ҷойгузин мешавад |
| 2026-08-05 | Огоҳии "deadline фардо" тавассути `BackgroundService`-и оддии .NET, на Hangfire | Hangfire барои фазаи 4 нигоҳ дошта шудааст (`01-architecture.md`); `IServiceScopeFactory` + `PeriodicTimer` барои ин фаза кофист |
| 2026-08-05 | Бахши тести SignalR: fake-и дастии `HubCallerContext`/`IGroupManager`/`IHttpContextFeature` (навъи воқеии SignalR: `Microsoft.AspNetCore.Http.Connections.Features.IHttpContextFeature`) | `Hub.Context`/`Hub.Groups` public setter доранд — барои тест 3.4 package-и mocking лозим нашуд |

## Масъалаҳои кушода

| # | Масъала | Масъул |
|---|---|---|
| 1 | Кадом рақам ба WhatsApp API меравад? | Faridun |
| 2 | Instagram ба Facebook Page пайваст шудааст? | Faridun |
