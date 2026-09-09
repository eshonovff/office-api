# Фазаи 9 — Дашборд

**Ҳадаф:** `GET /api/dashboard` — саҳифаи асосии office.nizom.tj бо маълумоти воқеӣ аз
ҷадвалҳои мавҷуда (ягон ҷадвали нав, ягон сутуни нав).
**Пешшарт:** Фазаи 6 (Инбокс), Фазаи 2 (Проект/таск) ✅

> ⚠️ **2026-08-26: ҳар се блок иҷро шуданд.** `GET /api/dashboard` (Блокҳои 1-2,
> `actionRequired`+`myWork`+`canSeeStats`) ва `GET /api/dashboard/stats` (Блоки 3,
> endpoint-и ҷудогона) — ҳарду тайёр.

## Дарун

- `GET /api/dashboard` — `actionRequired` (панҷ нишондиҳанда, ҳар кадом бо дастрасии дуруст,
  ниг. поён): `closingWindows`, `unassigned`, `failedMessages`, `channelIssues`, `overdueTasks`;
  `myWork` (ҳамеша шахсӣ, ҳатто барои Owner): `myConversations`, `myUnread`, `myTasksToday`,
  `myTasksOverdue`; `canSeeStats` (= `ChannelAccessGuard.CanSeeAllChannels`).
- `GET /api/dashboard/stats?days=7|14|30|90` — endpoint-и АЛОҲИДА (Owner/Admin танҳо, 403
  барои дигарон), ҳашт диаграмма: `volumeByDay`, `byChannel`, `operatorLoad`, `hourlyHeatmap`,
  `responseTimeBuckets`, `messageStatus`, `failureBreakdown`, `funnel`.

## Меъморӣ

- `Office.Api/Features/Dashboard/DashboardEndpoints.cs` — танҳо HTTP + кэш (60 сония дар
  хотира, `IMemoryCache`, калид `userId+нақшҳо`).
- `Office.Api/Features/Dashboard/DashboardQueryService.cs` — ҳисоби воқеӣ (scoped class,
  на static-и endpoint), то бе HTTP/кэш бевосита санҷида шавад (EF InMemory) — ҳамон
  сабабе, ки `InstagramContactProfileBackfillJob` як class аст, на қисми static.
- `Office.Api/Features/Dashboard/ChannelIssueReasonResolver.cs` — pure, тестшаванда: якчанд
  шарти `channelIssues` метавонанд ҳамзамон рост бошанд, матни якхела ҳамаашро дар бар мегирад.
- Ҳама COUNT/Take дар сатҳи SQL (`CountAsync`/`Take(5)`) — ягон `messages`/`conversations`-и
  пурра ба хотира кашида нашудааст.

## Дастрасӣ

- Ҳар нишондиҳандаи марбут ба чат (`closingWindows`, `unassigned`, `failedMessages`) аз
  ЯК `IChannelAccessGuard.ApplyAccessFilterAsync` мегузарад — як бор дар боло, баъд
  барои се нишондиҳанда такрор истифода мешавад (то дархости "only_assigned" дар DB се
  бор такрор нашавад).
- `channelIssues` — `IChannelAccessGuard.ApplyChannelAccessFilterAsync` (ҳамон формулаи
  `GET /api/channels/mine`).
- `overdueTasks` — вазифаҳо ба Project тааллуқ доранд, на ба Channel; ҳамон филтри
  `TasksEndpoints.ListAsync` (`ProjectAccessGuard.CanSeeAllProjects` + `Project.Members`).
- Тасдиқшуда бо тест: `only_assigned` операторе, ки узви канал аст, чати ба дигаре
  вогузошта ё умуман вогузошта-нашударо намебинад; Owner ҳама чизро мебинад.

## Навсозӣ 2026-08-26: failedMessages — гурӯҳбандӣ аз рӯи failure_code

`Message.FailureCode` (сутуни нав, "{PROVIDER}_{code}[_{subcode}]", масалан `FB_100_2018074`)
илова шуд — `MetaErrorCodeExtractor` (pure) онро аз ҷавоби хоми Meta мебарорад. Сабаб:
`fbtrace_id` (дар `FailureDetail`) дар ҳар дархост ЯГОНА аст — GROUP BY бар он бефоида буд
(20 хатои ноком = 20 гурӯҳи "беном"). `failedMessages.items` иваз шуд ба `failedMessages.groups`
(`GROUP BY failure_code` дар SQL, на LINQ-to-Objects) — натиҷаи воқеӣ бар DB-и production:
`14 × IG_2, 3 × FB_100_2018074, 2 × WA_131030, 1 × IG_1`. Матни фаҳмо аз `FailureCodeLabels`
(коди ношинос — худи код). Миграцияи `AddMessageFailureCode` ҳамаи 20 сатри мавҷударо бо як
UPDATE-и якхела (PL/pgSQL-и муваққатӣ, бе истисно партофтан) бозпур кард.

## Навсозӣ 2026-08-26 (2): ду ислоҳи хурд + Блоки 2 (myWork)

**А1 — оё failedMessages дар бораи чизи худҳалшуда огоҳ мекунад?** Тасдиқшуда бо код (на
ҳофиза), се ҷои коркарди хато: `MediaSendJob.cs:124-127`, `WhatsAppSendJob.cs:94` ва `:150-151`
— ҳар се дар роҳи муваффақ `message.DeliveryStatus = MessageDeliveryStatus.Sent;` мегузоранд.
Азбаски дашборди `failedMessages` танҳо `WHERE delivery_status = Failed`-ро мешуморад, паёме,
ки такрори худкори Hangfire (`AutomaticRetry`) муваффақ шуд, худ ба худ аз ҳисоб хориҷ мешавад —
**ҳеҷ ислоҳи код лозим набуд**. (Ёддошти хурд: `FailureCode`/`FailureDetail` дар роҳи муваффақ
тоза НАМЕШАВАД, танҳо `FailureReason` — маълумоти мурда мемонад дар сатри аллакай `Sent`, вале
ин ба ягон дархости ҷорӣ таъсир намерасонад, чунки ҳама аз рӯи `DeliveryStatus` филтр мекунанд.)

**А2 — коди ношиноси failure_code.** `IG_1` қасдан аз `FailureCodeLabels.ExactLabels` бардошта
шуд (як маротиба дида шудааст — кофӣ нест). `FailureCodeLabels.IsKnown(code)` илова шуд;
се ҷои коркарди хато (боло) ҳоло агар `FailureCode` ношинос бошад, `logger.LogWarning` бо
`RawResponseBody`-и пурра мегузоранд — то дафъаи оянда маълумот дошта бошем.

**Блоки 2 (myWork):** `accessibleConversations`-и Блоки 1 (аллакай ҳисобшуда) барои
`myConversations`/`myUnread` АЗ НАВ истифода мешавад — на дархости нави only_assigned. Ин ҳамон
ҷое, ки "муколамаи ба ман вогузошташуда, вале дар канале ки ман дастрасӣ надорам" худкор канда
мешавад (тасдиқшуда бо тест): `ApplyAccessFilterAsync` ин ҳолатро пеш аз `AssignedTo == userId`
хориҷ мекунад. `myUnread` = SUM(`Conversation.UnreadCount`) — ҳамон майдони мавҷуда, мантиқи нав
навишта нашуд. `myTasksToday`/`myTasksOverdue` ҳамон таърифи "сутуни хотимавӣ"-ро истифода
мебаранд, ки `overdueTasks`-и Блоки 1.

## Навсозӣ 2026-08-26 (3): се ислоҳи хурд + Блоки 3 (stats)

**А1 — failureCode бе матни тайёр.** `FailedMessageGroup.Label` аз DTO бардошта шуд — фронт
бисёрзабона аст (tg/ru), тарҷума бояд дар он ҷо бошад. `FailureCodeLabels` барои логи backend
мемонад (ниг. А2-и қаблӣ), ба API дигар намеравад.

**А2 — is_active аз channelIssues.** `ChannelIssueReasonResolver.Resolve` дигар параметри
`isActive` надорад; WHERE-и SQL низ `!c.IsActive ||`-ро бардошт. Санҷида зинда: `channelIssues.
count` аз 5 (панҷ канали тестии хомӯшкардашуда) ба **0** афтод.

**А3 — canSeeStats.** `DashboardResponse.CanSeeStats` (= `ChannelAccessGuard.CanSeeAllChannels`)
илова шуд. Фронт (`isOwnerOrAdmin(roles)` дар `app/config/permissions.ts`, истифодашуда дар
`dashboard/route.tsx` ва `dashboard/stats/route.tsx`) то ҳол мантиқи худро дорад — ба ин байрақ
гузаштан кори фронт (эҳтимол сессияи дигар, ки аллакай ин ду route-ро сохтааст) аст, на ин ҷо.

**Блоки 3 (`GET /api/dashboard/stats`):**
- `Office.Api/Features/Dashboard/StatsEndpoints.cs` — HTTP + кэши 5-дақиқагӣ (калид танҳо
  `days`, на userId — ҳама Owner/Admin ҳамон маълумоти якхелаи тим-вокеъро мебинанд, пас як
  cache entry барои ҳама кофист). `RequireOwnerOrAdmin()` (нав, `PermissionAuthorization.cs`) —
  `RequireRole(Owner, Admin)`, ҳамон дари `ChannelAccessGuard.CanSeeAllChannels`.
- `Office.Api/Features/Dashboard/DashboardStatsQueryService.cs` — ҳисоби воқеӣ. Дастрасии шахсӣ
  лозим НЕСТ (endpoint худаш Owner/Admin-ро талаб мекунад — ҳар кӣ инро даъват карда метавонад
  аллакай ҳама чизро мебинад).
- `Office.Api/Features/Dashboard/StatsDaysRange.cs`, `ResponseTimeBucketer.cs` — pure,
  тестшаванда.
- `OfficeLocalDate` васеъ шуд: `OfficeUtcOffsetHours` (const, барои дохили SQL-и EF LINQ —
  `m.CreatedAt.AddHours(OfficeUtcOffsetHours).Hour/.DayOfWeek/.Date`, тарҷумаи Npgsql санҷида
  зинда шуд), `LocalHour`/`LocalDayOfWeek` (танҳо барои тест — муқоиса бо натиҷаи SQL).
- Якҷоякунии round-trip: `byChannel`+`operatorLoad` аз ЯК дархост (ҳарду аз муколамаҳои кушода,
  ду гурӯҳбандии C# бар рӯи натиҷаи аллакай хурди SQL); `responseTimeBuckets`+`funnel` аз ЯК
  дархост (`let firstInbound.../let firstResponse...` — subquery-и корреллятсияшуда дар SQL,
  на N+1). Натиҷа: 8 диаграмма аз **6 round-trip**, на 8.
- `responseTimeBuckets`: "аввалин содиротии БАЪДИ аввалин воридотӣ" (на "аввалин содиротии
  умуман") — сохторан дарозии манфиро (муколамаи вайрон, ки ҷавобаш пеш аз воридотӣ омадааст)
  партофта мемонад, на ба ҳисоб мегирад; чунин муколама ҳамчун "бе ҷавоб" ҳисоб мешавад.
  `ResponseTimeBucketer.Bucket` низ дарозии манфиро истисно мепартояд (садди дуюм, барои
  ҳолате ки фарзияи боло хато баромад).

## Ҳадди "маълумот кофӣ" (sufficient)

| Диаграмма | Ҳадд | Сарчашма |
|---|---|---|
| `volumeByDay` | ≥7 рӯзи бо маълумот | спецификатсия |
| `responseTimeBuckets` | ≥20 муколамаи ҷавобдодашуда | спецификатсия |
| `hourlyHeatmap` | ≥100 паём | спецификатсия |
| `byChannel` | ≥5 муколамаи фаъол (ҷамъ) | интихоби худам |
| `operatorLoad` | ≥5 муколамаи кушода (ҷамъ) | интихоби худам |
| `messageStatus` | ≥20 паём | интихоби худам |
| `failureBreakdown` | ≥5 паёми ноком | интихоби худам (ҳадди паст — ҳатто якчанд хато диагностика-манфиатнок аст) |
| `funnel` | ≥10 муколама (Омад) | интихоби худам |

## Ҳисоб/дефинитсияҳои такроршуда (на аз худ навишташуда)

- "Кушода" (closingWindows) = `Status != ConversationStatus.Closed` — ҳамон шакле, ки
  `ConversationAutoReleaseJob`/`ConversationAssignmentPolicy` истифода мебаранд, на
  рӯйхати сахти `{New, InProgress, Waiting}`.
- "Сутуни хотимавӣ" (overdueTasks, myTasksToday, myTasksOverdue — ҲАМАИ се) = `BoardColumn.IsDoneColumn`.
- "Имрӯз" — **вақти маҳаллӣ (UTC+5), на UTC-и хом** (`OfficeLocalDate.Today`, ниг. поён барои
  сабаб). Ду ҷои истифода (`overdueTasks`-и Блоки 1 ва `myTasksToday`/`myTasksOverdue`-и Блоки 2)
  ҳамон "имрӯз"-и якхела мегиранд — як бор дар `ComputeAsync` ҳисоб мешавад, на дар ҳар метод.

## Индексҳо (миграцияи `AddDashboardIndexes`)

Пеш аз илова санҷида шуд — инҳо вуҷуд НАДОШТАНД:
- `conversations(status, assigned_to)` — индексҳои қаблӣ ё channel-мустақил буданд
  (`assigned_to` танҳо), ё `channel_id`-ро талаб мекарданд.
- `conversations(window_expires_at)` — ягон индекс бар ин сутун набуд.
- `messages(created_at, direction)` — индекси қаблӣ ба `conversation_id` баста буд.
- `messages(delivery_status)` — вуҷуд надошт (спецификатсия "messages(status)" мегӯяд,
  вале сутуни воқеӣ `delivery_status` аст, `status` номи дигаре нест).

## Кэш

`IMemoryCache`, 60 сония, калид `dashboard:{userId}:{нақшҳо, alphabetically}`. **Аввалин
истифодаи IMemoryCache дар ин лоиҳа** — то ин лаҳза ягон кэш дар Office.Api набуд.

## Натиҷаи ченкунӣ (маълумоти воқеӣ)

| Нишондиҳанда | Манбаи SQL | Тест дорад |
|---|---|---|
| `closingWindows` | `conversations` (status, window_expires_at, филтри дастрасӣ) | ✅ (empty/close-excluded/far-future-excluded) |
| `unassigned` | `conversations` (status=New, assigned_to=null, филтри дастрасӣ) | ✅ (only_assigned, Owner) |
| `failedMessages` | `messages` (delivery_status=Failed, 7 рӯз, GROUP BY failure_code) | ✅ (recent-only, гурӯҳбандӣ, коди ношинос) |
| `channelIssues` | `channels` (4 шарт бо OR, филтри дастрасии канал) | ✅ (яктогӣ, бисёршартӣ дар resolver) |
| `overdueTasks` | `tasks`+`board_columns` (due_date, is_done_column, аъзои project) | ✅ (done-column excluded, future excluded, ғайри-аъзо) |
| `myConversations`/`myUnread` | `conversations` (assigned_to=ман, гурӯҳ аз рӯи status, SUM unread_count) | ✅ (гурӯҳбандӣ, ҷамъ, дастрасии канал) |
| `myTasksToday`/`myTasksOverdue` | `tasks`+`board_columns` (assignee=ман, due_date маҳаллӣ, is_done_column) | ✅ (марзи нимшаб, done-column, аъзои project) |

**Вақти иҷро:** Блоки 1 танҳо — 95ms (бе кэш). Пас аз илова кардани Блоки 2 (6 дархости
иловагии SQL: 1 барои myConversations+myUnread якҷоя, 2×2 барои myTasksToday/Overdue) — 79ms
(бе кэш), 3.4ms (бо кэш). Фарқ дар ҳудуди тағйирёбии муқаррарии муҳити dev аст (на регрессия) —
ҳарду зери 300ms. accessibleConversations-и Блоки 1 барои myConversations аз нав истифода шуд —
дархости нави only_assigned илова НАШУД.

## Натиҷаи ченкунӣ — Блоки 3 (маълумоти воқеӣ, days=14, 2026-08-26)

| Диаграмма | Манбаи SQL | sampleSize | sufficient | Тест |
|---|---|---|---|---|
| `volumeByDay` | `messages.created_at`+`direction`, GROUP BY рӯзи маҳаллӣ | 9 | ✅ true | ✅ (марзи нимшаб, рӯзи холӣ) |
| `byChannel` | `conversations`→`channels` (кушода, is_active) | 42 | ✅ true | ✅ (inactive excluded, closed excluded) |
| `operatorLoad` | ҳамон бо `byChannel` (assigned_to) | 0 | ⬜ false | ✅ (dashboard-и воқеӣ ҳанӯз таъиноти зинда надорад) |
| `hourlyHeatmap` | `messages` воридотӣ, GROUP BY соат×рӯзи ҳафтаи маҳаллӣ | 300 | ✅ true | ✅ (вақти маҳаллӣ, на UTC) |
| `responseTimeBuckets` | аввалин воридотӣ→аввалин содиротии баъдӣ | 14 | ⬜ false | ✅ (бе ҷавоб, маълумоти вайрон, бакети воқеӣ) |
| `messageStatus` | `messages.delivery_status`, GROUP BY | 374 | ✅ true | ✅ (холӣ) |
| `failureBreakdown` | `messages.failure_code`, GROUP BY (ҳамон механизми А1) | 20 | ✅ true | ✅ (гурӯҳбандии давра) |
| `funnel` | `conversations` (омад/ҷавоб/баста, аз ҳамон таърифи Status) | 42 | ✅ true | ✅ (арзиши воқеӣ аз статус) |

**Вақти иҷро:** 97ms (days=14, бе кэш), 31ms (days=90, бе кэш — тағйирёбии муқаррарӣ, на
регрессия бо ҳаҷми калонтар), 4ms (бо кэш). Ҳама зери 500ms — EXPLAIN ANALYZE лозим нашуд.

**Кадом диаграммаҳо ҳоло "маълумот кам" мегӯянд (бо маълумоти воқеии production):**
`operatorLoad` (ҳеҷ чат ҳоло таъин нашудааст) ва `responseTimeBuckets` (14 аз 20-и лозимӣ) —
фронт барои ҳамин ду бояд "маълумот ҳанӯз кам аст" нависад, на график. Боқимонда 6 диаграмма
аллакай маънодоранд.

## Он чи хоста нашуд

Ҳеҷ чиз партофта нашуд — ҳамаи бандҳои се блок бо маълумоти мавҷуда пурра ҳисоб мешаванд.

## Вазифаҳо

- [x] 9.1 `GET /api/dashboard` — Блоки 1 (`actionRequired`): closingWindows, unassigned, failedMessages, channelIssues, overdueTasks
- [x] 9.2 Блоки 2 (`myWork`): myConversations, myUnread, myTasksToday, myTasksOverdue
- [x] 9.3 `GET /api/dashboard/stats` — Блоки 3: volumeByDay, byChannel, operatorLoad, hourlyHeatmap, responseTimeBuckets, messageStatus, failureBreakdown, funnel

## Definition of Done

- ✅ Ҳама нишондиҳанда аз ҷадвалҳои мавҷуда, бе миграцияи схема (танҳо индекс/сутуни хурд)
- ✅ Ҳисоб дар SQL (CountAsync/GroupBy/Take), на LINQ-to-Objects — ба ғайр аз бакетбандии
  responseTimeBuckets/funnel ва ду гурӯҳбандии byChannel/operatorLoad, ки қасдан бар рӯи
  натиҷаи АЛЛАКАЙ хурди SQL дар C# мешаванд (ҳуҷҷатнок карда шуд дар боло)
- ✅ Дастрасӣ тавассути IChannelAccessGuard/ProjectAccessGuard/RequireOwnerOrAdmin-и мавҷуда
- ✅ Кэши 60-сония (dashboard) ва 5-дақиқагӣ (stats)
- ✅ "Имрӯз"/минтақаи вақт — вақти маҳаллӣ (UTC+5) дар ҳар се ҷои истифода (overdueTasks,
  myTasksToday/Overdue, volumeByDay, hourlyHeatmap)
- ✅ Ҳашт диаграмма аз 6 round-trip, на 8
- ✅ Тестҳои воҳид (9 pure class) + интегратсионӣ (485 тест умуман, сабз)
- ✅ Вақти иҷро < 500ms бо маълумоти воқеӣ (ҳарду блок)
- ✅ Ҳадди "маълумот кам" барои ҳар ҳашт диаграмма, бо арзиши воқеии ҳозира
