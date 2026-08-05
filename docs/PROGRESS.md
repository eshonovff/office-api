# PROGRESS — ҳолати ҷорӣ

> Агент: ин файлро баъди ҳар фаза нав кун.

**Фазаи ҷорӣ:** `phase-1-auth`
**Ветка:** `feat/phase-1-auth`
**Санаи навсозӣ:** 2026-08-04

## Ҳолати фазаҳо

| Фаза | Ном | Ҳолат |
|---|---|---|
| 0 | Setup | ✅ тамом |
| 1 | Auth ва доступ | ✅ тамом |
| 2 | Проект ва таск | ⬜ нашуда |
| 3 | Realtime | ⬜ нашуда |
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

## Масъалаҳои кушода

| # | Масъала | Масъул |
|---|---|---|
| 1 | Кадом рақам ба WhatsApp API меравад? | Faridun |
| 2 | Instagram ба Facebook Page пайваст шудааст? | Faridun |
