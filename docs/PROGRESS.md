# PROGRESS — ҳолати ҷорӣ

> Агент: ин файлро баъди ҳар фаза нав кун.

**Фазаи ҷорӣ:** `phase-6-inbox`
**Ветка:** `dev`
**Санаи навсозӣ:** 2026-08-09

## Ҳолати фазаҳо

| Фаза | Ном | Ҳолат |
|---|---|---|
| 0 | Setup | ✅ тамом |
| 1 | Auth ва доступ | ✅ тамом |
| 2 | Проект ва таск | ✅ тамом |
| 3 | Realtime | ✅ тамом |
| 4 | Инфраструктураи каналҳо | ✅ тамом |
| 5 | WhatsApp | 🟡 дар кор — код тайёр, санҷиши зинда бо Meta мемонад |
| 6 | Инбокс | 🟡 дар кор — қисман (ниг. эзоҳи `docs/phases/phase-6-inbox.md`) |
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
| 2026-08-06 | `PlaceholderChannelProvider` — як синфи муваққатӣ барои ҳар се навъи канал (whatsapp/instagram/facebook) | Фазаи 4 "коди мушаххаси WhatsApp/IG/FB нанавис"-ро талаб мекунад, вале DoD бе pipeline-и воқеӣ санҷида намешавад. `VerifyWebhook`/`ParseWebhook` генералӣ (формати худсохта, на воқеии Meta); `SendMessage`/`MarkAsRead`/`DownloadMedia` `NotImplementedException` мепартоянд — фазаи 5/7 иваз мекунад |
| 2026-08-06 | Идентификатсияи канал аз webhook payload тавассути майдони `channelExternalId` (қарордоди худсохта) | `WebhookLog` тибқи `docs/04` `channel_id` надорад — бояд аз raw JSON муайян шавад; фазаи 5/7 ин қадамро ба формати воқеии Meta мутобиқ мекунад |
| 2026-08-06 | Idempotency: `MessageIdempotencyPlanner` (pure, тестшуда) + UNIQUE constraint дар DB ҳамчун ҳифзи дуюм | Ҳам ҷилавгирии дубликат дар сатҳи барнома (пеш аз навиштан), ҳам кафолати ниҳоии DB — агар race шавад ҳам |
| 2026-08-06 | `DELETE /api/channels/{id}` = soft (`is_active = false`) | Мутобиқ ба алгуи мавҷуда (Projects-и archive, Users-и active) — таърихи chat/conversation нигоҳ дошта мешавад |
| 2026-08-06 | Тозакунии `WebhookLog` (>30 рӯз) тавассути Hangfire recurring job, на `BackgroundService`-и фазаи 3 | Ҳоло Hangfire дастрас аст — истифодаи он барои ин кор табиист |
| 2026-08-06 | `/hangfire` ба рӯйхати path-ҳои JWT-аз-query-string (ҳамон алгуи `/hubs/*`-и фазаи 3) илова шуд | Dashboard-и Hangfire бо браузер кушода мешавад, на бо Authorization header |
| 2026-08-06 | Ҳозир деплой (фазаи 8) дар навбат нест — кор бо `localhost` идома меёбад | Frontend ва backend ҳоло дар як ҷо (локалӣ) кор мекунанд |
| 2026-08-06 | `users` бо `email`, `birth_date`, `address`, `gender`, `contract_document_*` васеъ карда шуд — истиснои қасдӣ аз рӯйхати "HR-НЕСТ"-и AGENTS.md | Ин майдонҳо барои профили onboarding/логин лозиманд (на модули пурраи HR — маош, ҳозирӣ). Корбар бевосита тасдиқ кард |
| 2026-08-06 | Логини корманди нав = рақами телефони нормализатсияшуда (`992XXXXXXXXX`); парол автоматӣ тавассути OsonSMS SMS мешавад ва як маротиба дар response низ нишон дода мешавад | Талаби корбар — на бо email/логини дастӣ, балки рақами телефон ҳамчун логин, то фаромӯш накунанд |
| 2026-08-06 | `ISmsSender`/`OsonSmsSender` (Office.Api/Sms) — HttpClient GET ба `sendsms_v1.php`, бе package-и нав | Протоколи OsonSMS (`str_hash = SHA256(txn_id;login;sender;phone;hash)`) тибқи амалисозиҳои маълуми PHP (`Rio-TJ/osonsms-gateway`) тасдиқ шуд; дар хатогии SMS корманд боз ҳам сохта мешавад (`SmsSent=false`), корбар парол дар экран мебинад |
| 2026-08-06 | Пароли муваққатӣ 8 рақами оддӣ (`PasswordGenerator.GenerateNumeric`), на 12 ҳарфу-рақоми омехта | Санҷиши зиндаи SMS тасдиқ кард — рақами оддӣ аз телефон дохил кардан осонтар аст |
| 2026-08-06 | `avatar_path` илова шуд (дар паҳлӯи `avatar_url`-и қаблӣ); `POST/GET /api/users/{id}/avatar` — endpoint-и нав, алгуи `contract-document` | `AvatarUrl` дар User пеш аз ин ягон upload/download надошт — комилан истифоданашуда буд; корбар дархост кард |
| 2026-08-06 | Owner: `must_change_password = false` доимӣ (ҳам дар `DbSeeder`, ҳам дар DB-и ҳозира); парол ба `12122002` собит карда шуд | Такрори reset+forced-change боиси фаромӯшкунии парол мешуд — корбар пароли доимӣ хост |
| 2026-08-06 | `POST /api/users` аз JSON ба `multipart/form-data` иваз шуд — расм (`avatar`, ихтиёрӣ) дар ҳамон дархости сохтани корманд меравад | Корбар мехост расмро дар вақти сохтан гузорад, на бо дархости алоҳидаи баъдӣ; валидатсия (FluentValidation) дастӣ, тавассути `IValidator<CreateUserRequest>`, дар дохили handler иҷро мешавад |
| 2026-08-06 | `UserListItem` (`GET /api/users`) бо тамоми майдонҳои профил (телефон, email, санаи таваллуд, адрес, ҷинсият, avatarUrl, hasContractDocument) пур карда шуд | Пеш танҳо id/fullName/username/isActive/roles дошт — frontend барои ҳар сатри рӯйхат маҷбур мешуд `GET /{id}` алоҳида занад |
| 2026-08-06 | `docs/employee-sms-api-changes.md` ба репозиторийи `office-web` кӯчонида шуд (корбар худаш кӯчонд) | Ҳуҷҷат барои frontend аст — акнун дар ҳамон репо зинда мемонад, на дар `office-api/docs` |
| 2026-08-06 | `Age` (int?) ба `UserListItem`/`UserDetail` илова шуд — сервер аз `BirthDate` ҳисоб мекунад (`AgeCalculator`, pure/тестшуда) | Frontend хост, ки синну сол омода бошад, на аз `birthDate` дар frontend ҳисоб карда шавад |
| 2026-08-07 | Фазаи 5: рақами воқеӣ ҳал нашуд — танҳо рақами тестии ройгони Meta истифода мешавад | Мисли фазаи 8 (Deploy), ин қарор ба оянда гузошта шуд; корбар тасдиқ кард |
| 2026-08-07 | `Channel.CredentialsEncrypted` (WhatsApp)-и шакли JSON: `{phoneNumberId, wabaId, accessToken}` | Пеш аз ин ягон шакл муайян нашуда буд (як string холӣ); `Webhooks:AppSecret`/`VerifyToken` глобалӣ мемонанд (як Meta App як маротиба) |
| 2026-08-07 | `IChannelProvider` бо 4 узви нав васеъ шуд: `ExtractChannelExternalId`, `ParseStatusUpdatesAsync`, `SendTemplateAsync`, `GetApprovedTemplatesAsync` | Конвенсияи `channelExternalId`-и фазаи 4 (placeholder) ба структураи воқеии Meta (`metadata.phone_number_id`) мутобиқ карда шуд; статус ва шаблон шаклҳои алоҳида доранд, ба `ParsedWebhookMessage` намеғунҷанд |
| 2026-08-07 | Мантиқи parse-и WhatsApp (`WhatsAppPayloadParser`) аз `WhatsAppProvider` ҷудо шуд — pure, бе DI | Барои тест бе сохтани тамоми занҷири DB/notification-и провайдер (алгуи `MessageIdempotencyPlanner`/`ConversationWindowCalculator`) |
| 2026-08-07 | Тирезаи 24-соата (5.8) тавассути худи хатогии Meta (код 131047) муайян мешавад, на санҷиши дастии DB дар провайдер | Meta худаш ин қоидаро татбиқ мекунад — такрор кардани мантиқ дар клиент coupling-и иловагӣ мебуд; `WhatsAppWindowClosedException` ин хатогиро аз дигар хатоҳо фарқ мекунад (барои 5.13 — retry намекунад) |
| 2026-08-07 | `POST /api/conversations/{id}/messages` (ҷавоб додан аз Inbox) сохта НАШУД — ин вазифаи 6.6 аст | Рӯйхати вазифаҳои фазаи 5 (5.1-5.13) endpoint-и ҷамъиятии фиристодан талаб намекунад; санҷиши DoD тавассути скрипти муваққатӣ (на API-и доимӣ) иҷро мешавад. Корбар тасдиқ кард |
| 2026-08-07 | Media-и воридотӣ (5.6) дар `whatsapp-media/{channelId}/{guid}` захира мешавад, `Message.MediaUrl` ба ин роҳи нисбӣ ишора мекунад (на URL-и оммавӣ) | Endpoint-и боргирии оммавӣ (`GET /api/messages/{id}/media`) вазифаи фазаи 6 аст — фазаи 5 танҳо файлро нигоҳ медорад |
| 2026-08-07 | `MessageType` бо `Location`/`Contact` васеъ шуд (миграция лозим нашуд — сутуни string) | Вазифаи 5.3 талаб мекунад, вале enum-и қаблӣ ин ду навъро надошт |
| 2026-08-08 | Артифактҳои деплой (`Dockerfile`, `docker-compose.prod.yml`, `deploy/nginx/office.nizom.tj.conf`, `deploy.sh`, `docs/deploy-runbook.md`) пеш аз фазаи 8 сохта шуданд — танҳо барои HTTPS-и воқеӣ (webhook-и Meta лозим дорад) | Ngrok/tunnel дар муҳити локалӣ бо шабакаи хеле суст кор накард; корбар VPS-и воқеӣ дошт (дар паҳлӯи NIZOM CRM зинда). Ҳама изолятсия шуд: network/volume/портҳои алоҳида (5100/5435, танҳо 127.0.0.1), конфигурат-и Nginx файли ҷудогона (сайти мавҷуда даст нарасид). Dockerfile-и Alpine-based локалӣ пурра санҷида шуд (build → up → migrations → health) пеш аз супоридан ба корбар |
| 2026-08-09 | Фазаи 6: танҳо зерфаҳриcти endpoint-ҳои дархостшуда сохта шуд (6.1, 6.3, 6.4, 6.6, 6.8+6.9, 6.17, 6.18) | Корбар бевосита рӯйхати маҳдудро дод, на ҳамаи 6.1-6.18. Боқимонда (6.2 cursor, 6.5 board, 6.7 notes, 6.10 read, 6.11 tags, 6.12-6.14 доступ, 6.15-6.16 CRUD-и шаблон) ба давраи оянда гузошта шуд — дар `docs/phases/phase-6-inbox.md` возеҳ сабт шуд |
| 2026-08-09 | Саҳифабандии `GET /api/conversations`/`.../messages` — `page`/`pageSize` (offset), на cursor | Дар кулли backend ҳеҷ ҷо алгуи cursor pagination мавҷуд набуд (Notifications/Comments `.Take(N)`-и оддӣ истифода мебаранд); offset содда ва мутобиқи услуби мавҷуда аст |
| 2026-08-09 | `PATCH /api/conversations/{id}/status` ва `.../assign`-и 6.8/6.9 ба **як** `PATCH /api/conversations/{id}` муттаҳид шуданд | Дархости корбар айнан ҳамин тавр буд ("change status and assigned user" — як PATCH). Пойгоҳи умумӣ `inbox.assign`; агар статус ба `Closed` иваз шавад, дохили handler санҷиши иловагии `inbox.close` тавассути `ClaimsPrincipal.HasPermission`-и нав иҷро мешавад |
| 2026-08-09 | `WhatsAppWindowClosedException` (аз `Office.Api.Channels.WhatsApp`) мустақим дар `ConversationsEndpoints` дастгирӣ мешавад → 409 | Корбар бевосита хост; coupling-и провайдер-мушаххас ба қабати generic-и Conversations қабулшуда аст, чунки тирезаи 24-соата мафҳуми хосси WhatsApp/Meta аст |
| 2026-08-09 | `WebhookProcessor` акнун `IInboxEventPublisher.MessageReceivedAsync`-ро барои ҳар паёми нави воридотӣ фиристад | Бе ин, танҳо ҷавобҳои худи оператор (аз `POST .../messages`) live буданд — паёми воридотии WhatsApp/IG/FB дар frontend то нав кардани саҳифа намоён намешуд |
| 2026-08-09 | Санҷиши воқеии зинда (на танҳо build/test) тавассути канали Instagram (`PlaceholderChannelProvider`-и фазаи 4) ва webhook-и имзошуда иҷро шуд, на WhatsApp-и воқеӣ | WhatsApp ҳанӯз тунели воқеӣ ба Meta надорад (фазаи 5 санҷиши зиндаашро интизор аст). Placeholder имкон дод тамоми pipeline (webhook → conversation/message → GET/PATCH/POST) бо `curl` санҷида шавад: `webhook_logs.error` холӣ, `window_expires_at` дуруст, PATCH assign+close кор кард, POST reply дар канали бе SendMessage амалисозишуда 500-и интизоршаванда дод (на 200-и бардурӯғ) |

## Масъалаҳои кушода

| # | Масъала | Масъул |
|---|---|---|
| 1 | Кадом рақам ба WhatsApp API меравад? | Faridun |
| 2 | Instagram ба Facebook Page пайваст шудааст? | Faridun |
