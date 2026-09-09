# PROGRESS — ҳолати ҷорӣ

> Агент: ин файлро баъди ҳар фаза нав кун. Манбаи ҳақиқат — `git log` ва код,
> на ин файл.

## Ҳолати имрӯза (2026-08-26)

**Тамом:** Фаза 0 (Setup), 1 (Auth), 2 (Проект/таск), 3 (Realtime), 4 (Инфраструктураи каналҳо).

**Ҳастем дар:** Фазаи 9 (Дашборд, ҳар се блок тамом — ниг. `docs/phases/phase-9-dashboard.md`),
дар паҳлӯи Фазаи 6 (Инбокс, ҳанӯз тартиб намонда) ва Фазаи 7 (Instagram+Facebook, ҳоло
воқеан кор мекунад — на placeholder).

> ⚠️ Сатрҳои 2026-08-13 то 2026-08-25 дар ҷадвали поён **сабт НАШУДААНД** (кор дар як чат
> дигар давом ёфт, ин файл нав нашуд — маҳз ин "кӯҳна шудан" буд). Ҳолати фазаҳои поён
> аз коди ҷорӣ дуруст аст, вале рӯйхати "чӣ/чаро"-и ҳаррӯза барои он давра нест.
> Хулосаи фишурда: Meta OAuth (Facebook Login for Business + Instagram API with Instagram
> Login, ду app-и ҷудогона), webhook (imзо, обунаи худкори Page/App сатҳ дар `/connect`),
> паём/media дар ҳарду самт барои Instagram/Facebook, echo-и is_echo (Outbound, на партофта),
> token refresh, backfill-и профили мижоз, ва як бор ислоҳи tunnel-и cloudflared (сабаби
> "ҳар се канал якбора хомӯш" — ниг. `docs/diag-2026-08-25-three-channels-down.md`).

**Се қадами навбатӣ:**
1. Frontend: диаграммаҳои Блоки 3 (`dashboard/stats/route.tsx` то ҳол `<div />`-и холист, тибқи нақша), гузариш ба `canSeeStats`-и бэкенд ба ҷои `isOwnerOrAdmin(roles)`-и такрории frontend
2. 6.11 (тег), 6.5 (board/Kanban-и чатҳо), CRUD-и `message_templates` (6.15-6.16) — ҳанӯз аз рӯйхати 6.1-6.18 намондаанд
3. Фаза 8 (Deploy) — деплойи расмии тамоми система (Instagram/Facebook аллакай воқеӣ шуданд, деплой то ҳол танҳо барои WhatsApp-и санҷишӣ буд)

## Ҳолати фазаҳо

| Фаза | Ном | Ҳолат |
|---|---|---|
| 0 | Setup | ✅ тамом |
| 1 | Auth ва доступ | ✅ тамом |
| 2 | Проект ва таск | ✅ тамом |
| 3 | Realtime | ✅ тамом |
| 4 | Инфраструктураи каналҳо | ✅ тамом |
| 5 | WhatsApp | 🟡 фиристодан/қабул/статус/media бо WhatsApp-и воқеӣ дар сервер тасдиқ шуд; рақами доимӣ (на тестии Meta) ҳанӯз не |
| 6 | Инбокс | 🟡 асосӣ тамом; board, tags, cursor pagination, CRUD-и шаблон намонда |
| 7 | Instagram + Facebook | 🟡 воқеан кор мекунад (OAuth, webhook, паём/media дар ҳарду самт, echo) — вале фиристодани media-и Instagram то App Review-и Meta нагузарад бастааст (`ChannelCapabilities.CanSendMedia`), ниг. `docs/PROGRESS.md`-и таърихӣ дар `git log` барои тафсил |
| 8 | Deploy | 🟡 артефактҳо сохта, як маротиба барои WhatsApp истифода шуд — деплойи расмии IG/FB нест |
| 9 | Дашборд | 🟡 Backend-и ҳар се блок (`actionRequired`, `myWork`, `GET /api/dashboard/stats`) тамом ва тасдиқшуда; диаграммаҳои frontend-и Блоки 3 намонда — ниг. `docs/phases/phase-9-dashboard.md` |

Ҳолатҳо: ⬜ нашуда · 🟡 дар кор · ✅ тамом · ⛔ басташуда

## Холигиҳои backend ↔ frontend

Санҷида бо муқоисаи ҳамаи endpoint-ҳои `Office.Api` (grep-и `MapGet/Post/Put/Patch/Delete`)
ба `office-web/app/api/*.ts`. Аксарият як-ба-як мувофиқанд; фарқҳои воқеӣ:

**Backend дорад, frontend UI истифода намебарад:**
- `GET /api/conversations?assignedUserId=` — параметр дар DTO-и frontend
  (`ConversationsListParams.assignedUserId`) ҳаст, вале дар UI-и Инбокс
  (`ConversationList.tsx`) филтри "аз рӯи корманд" сохта нашудааст — танҳо
  channel ва status филтр доранд.
- `GET/POST/PATCH/DELETE /api/webhooks/{provider}` — ба назардошт, ин
  барои Meta аст, на фронтенд (табиист).

**Frontend чизе намедорад, ки backend низ надорад (мутобиқ, на холигӣ):**
- board/Kanban-и чатҳо, тег, CRUD-и шаблон — на backend, на frontend
  (6.5/6.11/6.15-6.16 воқеан нашудаанд, на танҳо фаромӯш дар фронтенд).

**Дигар ҳама** (channels, users, roles, projects/columns/labels, tasks/comments/
attachments/activity, notifications, delayed-send+cancel, internal notes,
takeover, assignment-history, media/voice-note/thumbnail, auth+refresh) —
дар ҳарду тараф пурра васлшуда.

## Масъалаҳои кушода

| # | Масъала | Масъул |
|---|---|---|
| 1 | Кадом рақам ба WhatsApp API меравад? | Faridun |
| 2 | Instagram ба Facebook Page пайваст шудааст? | Faridun |

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
| 2026-08-06 | Ҳозир деплой (фазаи 8) дар навбат нест — кор бо `localhost` идома меёбад | Frontend ва backend ҳоло дар як ҷо (локалӣ) кор мекунанд *(баъдтар, 2026-08-08, деплой-и воситавӣ барои санҷиши WhatsApp сохта шуд — ниг. поён)* |
| 2026-08-06 | `users` бо `email`, `birth_date`, `address`, `gender`, `contract_document_*` васеъ карда шуд — истиснои қасдӣ аз рӯйхати "HR-НЕСТ"-и AGENTS.md | Ин майдонҳо барои профили onboarding/логин лозиманд (на модули пурраи HR — маош, ҳозирӣ). Корбар бевосита тасдиқ кард |
| 2026-08-06 | Логини корманди нав = рақами телефони нормализатсияшуда (`992XXXXXXXXX`); парол автоматӣ тавассути OsonSMS SMS мешавад ва як маротиба дар response низ нишон дода мешавад | Талаби корбар — на бо email/логини дастӣ, балки рақами телефон ҳамчун логин, то фаромӯш накунанд |
| 2026-08-06 | `ISmsSender`/`OsonSmsSender` (Office.Api/Sms) — HttpClient GET ба `sendsms_v1.php`, бе package-и нав | Протоколи OsonSMS (`str_hash = SHA256(txn_id;login;sender;phone;hash)`) тибқи амалисозиҳои маълуми PHP (`Rio-TJ/osonsms-gateway`) тасдиқ шуд; дар хатогии SMS корманд боз ҳам сохта мешавад (`SmsSent=false`), корбар парол дар экран мебинад |
| 2026-08-06 | Пароли муваққатӣ 8 рақами оддӣ (`PasswordGenerator.GenerateNumeric`), на 12 ҳарфу-рақоми омехта | Санҷиши зиндаи SMS тасдиқ кард — рақами оддӣ аз телефон дохил кардан осонтар аст |
| 2026-08-06 | `avatar_path` илова шуд (дар паҳлӯи `avatar_url`-и қаблӣ); `POST/GET /api/users/{id}/avatar` — endpoint-и нав, алгуи `contract-document` | `AvatarUrl` дар User пеш аз ин ягон upload/download надошт — комилан истифоданашуда буд; корбар дархост кард |
| 2026-08-06 | `POST /api/users` аз JSON ба `multipart/form-data` иваз шуд — расм (`avatar`, ихтиёрӣ) дар ҳамон дархости сохтани корманд меравад | Корбар мехост расмро дар вақти сохтан гузорад, на бо дархости алоҳидаи баъдӣ; валидатсия (FluentValidation) дастӣ, тавассути `IValidator<CreateUserRequest>`, дар дохили handler иҷро мешавад |
| 2026-08-06 | `UserListItem` (`GET /api/users`) бо тамоми майдонҳои профил (телефон, email, санаи таваллуд, адрес, ҷинсият, avatarUrl, hasContractDocument) пур карда шуд | Пеш танҳо id/fullName/username/isActive/roles дошт — frontend барои ҳар сатри рӯйхат маҷбур мешуд `GET /{id}` алоҳида занад |
| 2026-08-06 | `docs/employee-sms-api-changes.md` ба репозиторийи `office-web` кӯчонида шуд (корбар худаш кӯчонд) | Ҳуҷҷат барои frontend аст — акнун дар ҳамон репо зинда мемонад, на дар `office-api/docs` |
| 2026-08-06 | `Age` (int?) ба `UserListItem`/`UserDetail` илова шуд — сервер аз `BirthDate` ҳисоб мекунад (`AgeCalculator`, pure/тестшуда) | Frontend хост, ки синну сол омода бошад, на аз `birthDate` дар frontend ҳисоб карда шавад |
| 2026-08-07 | Фазаи 5: рақами воқеӣ ҳал нашуд — танҳо рақами тестии ройгони Meta истифода мешавад | Мисли фазаи 8 (Deploy), ин қарор ба оянда гузошта шуд; корбар тасдиқ кард (то ҳол ҳамин тавр аст, ниг. масъалаи кушодаи №1) |
| 2026-08-07 | `Channel.CredentialsEncrypted` (WhatsApp)-и шакли JSON: `{phoneNumberId, wabaId, accessToken}` | Пеш аз ин ягон шакл муайян нашуда буд (як string холӣ); `Webhooks:AppSecret`/`VerifyToken` глобалӣ мемонанд (як Meta App як маротиба) |
| 2026-08-07 | `IChannelProvider` бо 4 узви нав васеъ шуд: `ExtractChannelExternalId`, `ParseStatusUpdatesAsync`, `SendTemplateAsync`, `GetApprovedTemplatesAsync` | Конвенсияи `channelExternalId`-и фазаи 4 (placeholder) ба структураи воқеии Meta (`metadata.phone_number_id`) мутобиқ карда шуд; статус ва шаблон шаклҳои алоҳида доранд, ба `ParsedWebhookMessage` намеғунҷанд |
| 2026-08-07 | Мантиқи parse-и WhatsApp (`WhatsAppPayloadParser`) аз `WhatsAppProvider` ҷудо шуд — pure, бе DI | Барои тест бе сохтани тамоми занҷири DB/notification-и провайдер (алгуи `MessageIdempotencyPlanner`/`ConversationWindowCalculator`) |
| 2026-08-07 | Тирезаи 24-соата (5.8) тавассути худи хатогии Meta (код 131047) муайян мешавад, на санҷиши дастии DB дар провайдер | Meta худаш ин қоидаро татбиқ мекунад — такрор кардани мантиқ дар клиент coupling-и иловагӣ мебуд; `WhatsAppWindowClosedException` ин хатогиро аз дигар хатоҳо фарқ мекунад (барои 5.13 — retry намекунад) |
| 2026-08-07 | `POST /api/conversations/{id}/messages` (ҷавоб додан аз Inbox) сохта НАШУД — ин вазифаи 6.6 аст | Рӯйхати вазифаҳои фазаи 5 (5.1-5.13) endpoint-и ҷамъиятии фиристодан талаб намекунад; санҷиши DoD тавассути скрипти муваққатӣ (на API-и доимӣ) иҷро мешавад. Корбар тасдиқ кард |
| 2026-08-07 | Media-и воридотӣ (5.6) дар `whatsapp-media/{channelId}/{guid}` захира мешавад, `Message.MediaUrl` ба ин роҳи нисбӣ ишора мекунад (на URL-и оммавӣ) | Endpoint-и боргирии оммавӣ (`GET /api/messages/{id}/media`) вазифаи фазаи 6 аст — фазаи 5 танҳо файлро нигоҳ медорад |
| 2026-08-07 | `MessageType` бо `Location`/`Contact` васеъ шуд (миграция лозим нашуд — сутуни string) | Вазифаи 5.3 талаб мекунад, вале enum-и қаблӣ ин ду навъро надошт |
| 2026-08-08 | Артифактҳои деплой (`Dockerfile`, `docker-compose.prod.yml`, `deploy/nginx/office.nizom.tj.conf`, `deploy.sh`, `docs/deploy-runbook.md`) пеш аз фазаи 8 сохта шуданд — танҳо барои HTTPS-и воқеӣ (webhook-и Meta лозим дорад) | Ngrok/tunnel дар муҳити локалӣ бо шабакаи хеле суст кор накард; корбар VPS-и воқеӣ дошт (дар паҳлӯи NIZOM CRM зинда). Ҳама изолятсия шуд: network/volume/портҳои алоҳида (5100/5435, танҳо 127.0.0.1), конфигурат-и Nginx файли ҷудогона (сайти мавҷуда даст нарасид). Dockerfile-и Alpine-based локалӣ пурра санҷида шуд (build → up → migrations → health) пеш аз супоридан ба корбар |
| 2026-08-09 | Фазаи 6: танҳо зерфаҳриcти endpoint-ҳои дархостшуда сохта шуд (6.1, 6.3, 6.4, 6.6, 6.8+6.9, 6.17, 6.18) | Корбар бевосита рӯйхати маҳдудро дод, на ҳамаи 6.1-6.18. Боқимонда (6.2 cursor, 6.5 board, 6.7 notes, 6.11 tags, 6.12-6.14 доступ, 6.15-6.16 CRUD-и шаблон) ба давраи оянда гузошта шуд — 6.7/6.10/6.12-6.14 баъдтар иҷро шуданд (ниг. поён) |
| 2026-08-09 | Саҳифабандии `GET /api/conversations`/`.../messages` — `page`/`pageSize` (offset), на cursor | Дар кулли backend ҳеҷ ҷо алгуи cursor pagination мавҷуд набуд (Notifications/Comments `.Take(N)`-и оддӣ истифода мебаранд); offset содда ва мутобиқи услуби мавҷуда аст |
| 2026-08-09 | `PATCH /api/conversations/{id}/status` ва `.../assign`-и 6.8/6.9 ба **як** `PATCH /api/conversations/{id}` муттаҳид шуданд | Дархости корбар айнан ҳамин тавр буд ("change status and assigned user" — як PATCH). Пойгоҳи умумӣ `inbox.assign`; агар статус ба `Closed` иваз шавад, дохили handler санҷиши иловагии `inbox.close` тавассути `ClaimsPrincipal.HasPermission`-и нав иҷро мешавад |
| 2026-08-09 | `WhatsAppWindowClosedException` (аз `Office.Api.Channels.WhatsApp`) мустақим дар `ConversationsEndpoints` дастгирӣ мешавад → 409 | Корбар бевосита хост; coupling-и провайдер-мушаххас ба қабати generic-и Conversations қабулшуда аст, чунки тирезаи 24-соата мафҳуми хосси WhatsApp/Meta аст |
| 2026-08-09 | `WebhookProcessor` акнун `IInboxEventPublisher.MessageReceivedAsync`-ро барои ҳар паёми нави воридотӣ фиристад | Бе ин, танҳо ҷавобҳои худи оператор (аз `POST .../messages`) live буданд — паёми воридотии WhatsApp/IG/FB дар frontend то нав кардани саҳифа намоён намешуд |
| 2026-08-09 | Санҷиши воқеии зинда (на танҳо build/test) тавассути канали Instagram (`PlaceholderChannelProvider`-и фазаи 4) ва webhook-и имзошуда иҷро шуд, на WhatsApp-и воқеӣ | WhatsApp ҳанӯз тунели воқеӣ ба Meta надорад (фазаи 5 санҷиши зиндаашро интизор аст). Placeholder имкон дод тамоми pipeline (webhook → conversation/message → GET/PATCH/POST) бо `curl` санҷида шавад: `webhook_logs.error` холӣ, `window_expires_at` дуруст, PATCH assign+close кор кард, POST reply дар канали бе SendMessage амалисозишуда 500-и интизоршаванда дод (на 200-и бардурӯғ) |
| 2026-08-09 | Санҷиши зиндаи WhatsApp-и воқеӣ дар сервер (office.nizom.tj): фиристодан ва қабул тасдиқ шуд | Корбар бевосита дар сервер санҷид (deploy-и `docs/deploy-runbook.md`, канали воқеии WhatsApp тавассути `POST /api/channels`). Статуси `delivered`/`read`, нусхабардории media баъди мӯҳлат, ва рафтори тирезаи 24-соата ҳанӯз алоҳида тасдиқ нашудаанд — фазаи 5/6 то ҳол ✅ пурра нест |
| 2026-08-09 | Филтри доступи 6.12-6.14 (`channel_members`/`only_assigned`) илова шуд: `Office.Api/Common/ConversationAccessResolver.cs` (pure, тестшуда — алгуи `PermissionResolver`) + `ChannelAccessGuard`/`IChannelAccessGuard` (DB-backed, алгуи `ProjectAccessGuard`, як ҷои умумӣ барои ҳамаи 5 endpoint-и Conversations) | `CanSeeAllChannels` = Owner/Admin (ҳамон формулаи `ProjectAccessGuard.CanSeeAllProjects`, permission-и нав илова нашуд). Санҷидашуда бо `curl` бо корманди дуюм (SQL, бе SMS-и воқеӣ): узви канал не → 404/холӣ; узв шуд → намоён; `only_assigned=true` ва таъиннашуда → боз 404/холӣ; таъин шуд → намоён |
| 2026-08-09 | 6.10 `POST /api/conversations/{id}/read` илова шуд — паёмҳои воридотии `Read`-нашуда → `Read`, `unreadCount` → 0, permission `inbox.view`, ҳамон `ChannelAccessGuard` | Event-и мавҷуда барои "read" мустақим намеғунҷад (4-тои `IInboxEventPublisher` танҳо MessageReceived/Sent/Assigned/StatusChanged); `ConversationStatusChangedAsync` ҳамчун сигнали умумии "чат нав шуд" такрор истифода шуд (payload = `ConversationDetail`-и пурра бо `unreadCount:0`) — event-и нав илова накардам |
| 2026-08-10 | Дастгирии пурраи media-и Inbox (қабул/фиристодан/нигоҳдорӣ) — на дар рӯйхати рақамдори фазаи 5/6, вазифаи алоҳидаи корбар | Корбар бевосита хост: боркунии боэътимоди media-и воридотӣ, фиристодани замима/voice note, тозакунии худкор. Ҳамаи 10 commit дар `feat/inbox-media` бо WhatsApp-и воқеӣ санҷида шуд (сурат, voice note — ҳарду ба телефон омаданд, санҷидашуда аз ҷониби корбар) |
| 2026-08-10 | Танҳо ffmpeg/ffprobe барои thumbnail, transcode ва пурсиши давомнокӣ — package-и нави тасвир (SixLabors.ImageSharp) илова НАШУД | Корбар бевосита интихоб кард: ffmpeg аллакай барои voice note лозим буд, package-и иловагӣ лозим намонд. Се шарти ҳатмии корбар риоя шуд: (1) `ProcessStartInfo.ArgumentList` танҳо, ҳеҷ гоҳ сатри фармони ҳамҷояшуда, (2) timeout 30 сония бо куштани process дар хатогӣ/анҷоми вақт, (3) навбати алоҳидаи Hangfire (`media`, `WorkerCount=2`) |
| 2026-08-10 | Боркунии media-и воридотӣ ба `MediaDownloadJob`-и Hangfire (навбати `media`, 5 такрор бо backoff то 6 соат) кӯчонида шуд | Media id-и Meta пас аз ~5 дақиқа эътибор надорад ва файл пас аз 30 рӯз нест мешавад — боркунии боэътимод бо такрор лозим аст. Хатогӣ дар `Message.MediaDownloadError` сабт мешавад |
| 2026-08-10 | Фиристодани media (замима/voice note) пурра асинхронӣ — endpoint 202 бармегардонад, `MediaSendJob` дар навбати `media` кор мекунад | Voice note transcode ва боркунии файли калон метавонад чанд сония тӯл кашад — дархости HTTP набояд интизор шавад |
| 2026-08-10 | `WhatsAppProvider.SendMediaMessageAsync` акнун wamid-ро бармегардонад, `MediaSendJob` онро дар `Message.ExternalId` сабт мекунад | Ошкор шуд дар санҷиши зинда: пеш аз ин статуси webhook (`delivered`/`read`) ҳеҷ гоҳ ба паёми содиротӣ мувофиқ намеомад (`external_id = null`). Роҳи матни оддӣ ҳанӯз ин мушкилро дорад (берун аз доираи ин вазифа) |
| 2026-08-10 | Нигоҳдории media вобаста ба навъ (`MediaRetentionCleanupJob`, recurring Hangfire, ҳаррӯза): расм/овоз/ҳуҷҷат 365 рӯз, видео 7 рӯз | Файл нест мешавад, вале сатри `Message` ва thumbnail не — `MediaDeletedAt` танзим мешавад |
| 2026-08-10 | `UploadsPathResolver` (Common/) — мантиқи такрории ҳалли `Uploads:RootPath` ба як ҷо ҷамъ шуд | Бо илова шудани 3 истифодабарандаи нав такрор аз 3 зиёд шуд — вақти ҷудо кардан расид |
| 2026-08-11 | `GET /api/channels/mine` барои операторони `only_assigned` танҳо каналҳои таъиншударо бармегардонад; `WhatsApp templates`/channel-detail ба `inbox` (на `admin`) кӯчид | Frontend-и Инбокс ба ин endpoint-ҳо ниёз дошт, вале доступашон дар доираи "Admin" маҳдуд буд |
| 2026-08-11 | CORS: origin-ҳои иҷозатшуда аз `appsettings`/конфигуратсия хонда мешаванд, на hardcode | Деплой дар домейни воқеӣ (`office.nizom.tj`) талаб мекард |
| 2026-08-11 | `Task.DueDate` date-only (аллакай тарҳрезишуда буд), вале bug дар mapping-и `move`-и предшественник/пайрав ислоҳ шуд; хатогии JSON-и клиент дигар ба 500 фурӯ намеравад | Ду bug-и ҷудогона, дар `docs/bug-*.md` сабт шуда |
| 2026-08-12 | `Message.SentByUserName` snapshot мешавад дар вақти фиристодан; FK-и conversation→channel сахттар шуд | Агар корманд баъдтар нест/иваз шавад, номи фиристанда дар паёми кӯҳна набояд гум шавад |
| 2026-08-12 | `GET /api/channels/{id}/assignable-users` илова шуд — барои филтри "таъин кардан ба..." дар Инбокс | Frontend-ро лозим буд рӯйхати кормандони узви ҳамон канал, на ҳамаи корманд |
| 2026-08-12 | Таъин ба корманди бе доступи канал рад мешавад (`PATCH /conversations/{id}`) | Bug: пеш аз ин ҳар корманд метавонист таъин шавад, ҳатто агар узви `channel_members` набошад |
| 2026-08-12 | Claim-and-hold: чат ба operator-и якум, ки ҷавоб медиҳад, худкор таъин мешавад (`ConversationAssignmentPolicy.ShouldClaimOnReply`) | Пеш аз ин ҳама операторони узви канал метавонистанд ба як мижоз ҳамзамон ҷавоб диҳанд — coordination дастӣ лозим буд |
| 2026-08-12 | Operator-и ғайри-масъул ба чати таъиншуда фақат хонданӣ мешавад; `POST /conversations/{id}/takeover` барои гирифтани доступ илова шуд | Мушобеҳи бетартибии дучандаи ҷавоб — акнун ошкоро "takeover" лозим аст, тасодуфӣ рух намедиҳад |
| 2026-08-12 | Auto-release: чат баъди бефаъолиятии assignee (`ConversationAutoReleaseJob`, Hangfire recurring) худкор ба ҳавз бармегардад | Чат ба як корманд то абад "гаравгон" намонад агар ӯ фаромӯш кунад ё офлайн шавад |
| 2026-08-12 | Delayed send (45с, `Inbox:DelayedSendSeconds`) бо cancel: паём фавран `Pending` сабт мешавад, баъд бо таъхир ба провайдер меравад — `POST .../messages/{messageId}/cancel` дар ин тиреза бекор мекунад | Имкони "undo" пеш аз расидан ба мижоз — хатои таппиш/матни нодуруст қобили ислоҳ |
| 2026-08-12 | Internal note (`isInternalNote` дар `SendMessageRequest`) — ба мижоз намеравад ҳеҷ гоҳ (`InternalNoteGuard.CanDispatchToProvider` дар худи `WhatsAppSendJob`, на танҳо дар endpoint), claim намекунад, unread/тиреза-24с таъсир намекунад | 6.7-и қаблӣ; сутуни `messages.is_internal_note` дар DB буд, вале ҳеҷ роҳе барои танзими он набуд — маълумоти мурда буд |
| 2026-08-13 | `GET /api/conversations/{id}/assignment-history` илова шуд — рӯйхати воқеаҳои таъин (`ClaimedOnReply`/`Takeover`/`Reassigned`/`AutoReleased`) | Барои шаффофият: кӣ, кай ва чаро чатро гирифт/супурд |
| 2026-08-13 то 2026-08-25 | *(сабт нашуд — ин файл дар ин давра нав нашуд)* Фаза 7 (Instagram+Facebook) воқеан сохта шуд: Meta OAuth, webhook (имзо/обуна), паём/media дар ҳарду самт, echo, token refresh, backfill-и профил, ислоҳи tunnel | Тафсил дар `git log`, `docs/diag-2026-08-25-three-channels-down.md` |
| 2026-08-26 | `GET /api/dashboard` — Блоки 1 (`actionRequired`): closingWindows/unassigned/failedMessages/channelIssues/overdueTasks, ҳама аз SQL (CountAsync/Take), `IChannelAccessGuard`/`ProjectAccessGuard`-и мавҷуда, кэши 60с (`IMemoryCache`-и аввалин дар лоиҳа), 4 индекси нав | Тартиби корӣ: Блоки 1 аввал, тасдиқ, баъд Блоки 2/3 — ниг. `docs/phases/phase-9-dashboard.md` |
| 2026-08-26 | `ChannelCapabilities.CanSendMedia/CanSendVoice(Facebook)` → true — санҷиши зинда бо скрипти мустақил (бе Hangfire, токени production): 5/5 маротиба (сурати хурд, audio/mp4-и ба андозаи voice note, PNG-и калон) message_attachments+POST/messages ҳарду 200 доданд. 3 хатои DB-ии subcode 2018074 хулосаи "banди Meta" НАБУД — механизм 100% кор мекунад, ду аз се хато 3 сония аз ҳам дур буданд (ҳодисаи муваққатии тарафи Meta) | Гипотезаи "URL, на bytes" рад шуд (код аллакай bytes мефиристод); Instagram-и банди тасдиқшуда бо ин омехта нашавад |
| 2026-08-26 | `messages.failure_code` (нав, "{PROVIDER}_{code}[_{subcode}]", `MetaErrorCodeExtractor`-и pure) — дашборди `failedMessages` акнун аз рӯи он GROUP BY мекунад (`14×IG_2, 3×FB_100_2018074, 2×WA_131030, 1×IG_1`), на 20 сатри JSON-и "беном" | `fbtrace_id` дар ҳар дархост ягона аст — GROUP BY бар FailureReason/Detail бефоида буд. Backfill — як UPDATE-и якхела (PL/pgSQL-и муваққатӣ), тайёр барои ҳаҷми калон |
| 2026-08-26 | А1: тасдиқшуда бо код — паёми Failed баъди retry-и муваффақ ба Sent бармегардад (се ҷои коркарди хато, ҳама се). Дашборд ислоҳ лозим надошт. А2: `IG_1` аз харитаи "маълум" бардошта шуд (як маротиба дида шудааст) — акнун коди ношинос логи Warning бо payload-и пурра мегузорад | Дашборд набояд дар бораи чизи худҳалшуда огоҳ кунад; барои коди ношинос бори дигар маълумот лозим |
| 2026-08-26 | Дашборд Блоки 2 (`myWork`): myConversations/myUnread/myTasksToday/myTasksOverdue — ҳамеша шахсӣ (ҳатто Owner). `OfficeLocalDate` (UTC+5) илова шуд — `overdueTasks`-и Блоки 1 низ ба он гузашт (пеш UTC-и хом буд) | Ҳамон `accessibleConversations`-и Блоки 1 аз нав истифода шуд — дархости нави only_assigned нест; "имрӯз" бояд дар ҳарду блок якхела бошад |
| 2026-08-26 | А1: `FailedMessageGroup.Label` аз API бардошта шуд (фронт бисёрзабона аст, тарҷума дар он ҷо). А2: `is_active` аз `channelIssues` бардошта шуд — 5 канали тестии хомӯшкардашуда дигар "мушкил" нишон намедиҳанд (5→0 бо маълумоти воқеӣ). А3: `canSeeStats` ба `/api/dashboard` илова шуд (=`CanSeeAllChannels`) | Дашборд бисёрзабона аст, на танҳо tg; канали қасдан ғайрифаъол мушкил нест; фронт набояд мантиқи RoleKeys-ро такрор нависад |
| 2026-08-26 | `GET /api/dashboard/stats` (Блоки 3, Owner/Admin танҳо, 403 барои дигарон, кэши 5 дақ) — ҳашт диаграмма (volumeByDay, byChannel, operatorLoad, hourlyHeatmap, responseTimeBuckets, messageStatus, failureBreakdown, funnel) аз 6 round-trip. Ҳар диаграмма `sampleSize`/`sufficient` дорад — 2 аз 8 (operatorLoad, responseTimeBuckets) ҳоло "маълумот кам" мегӯянд бо маълумоти воқеии production | `RequireOwnerOrAdmin()` (нав) — ҳамон дари `ChannelAccessGuard.CanSeeAllChannels`; `OfficeLocalDate.AddHours(5)` дар дохили LINQ санҷида шуд, ки ба Npgsql SQL тарҷума мешавад |
