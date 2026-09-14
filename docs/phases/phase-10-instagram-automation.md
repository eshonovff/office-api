# Фазаи 10 — Автоматизатсияи коментарии Instagram (V1)

**Ҳадаф:** Вақте касе дар пости Instagram коментарий мегузорад, система метавонад худкор
ба коментарий ҷавоб диҳад (public reply) ва ба муаллифи он DM (private reply) фиристад —
бар асоси қоидаи калимаи калидӣ, cooldown ва филтри ҳалқаи худ-ба-худ.
**Пешшарт:** Фазаи 7 (Instagram Messaging, OAuth, webhook pipeline) ✅

> ✅ **2026-09-14: пурра иҷро шуд ва бо аккаунти воқеии production санҷида шуд** (webhook,
> Graph API-и воқеӣ, обунаи webhook). Ниг. "Санҷиши зинда" поён.

## Маҳдудиятҳои Meta (тасдиқшуда пеш аз код, ниг. ҳуҷҷати расмии Meta 2026-09-14)

- **Public reply** (`POST /{comment-id}/replies`, параметри `message`): scope
  `instagram_business_manage_comments` (аллакай дархост шудааст дар
  `InstagramOAuthConnector.cs` аз Фазаи 7 — тасдиқшуда, ин scope то ин фаза истифода
  намешуд). Ягон маҳдудияти шумора надорад.
- **Private reply** (`POST /{ig-id}/messages` бо `recipient: {comment_id}`):
  - Танҳо **ЯК бор** барои як коментарий — кӯшиши дуюм хато медиҳад.
  - Танҳо дар давоми **7 РӮЗ** пас аз сохта шудани коментарий (барои пости
    оддӣ/reel/ads — на Instagram Live, ки танҳо то анҷоми пахш кор мекунад).
  - Ин ду маҳдудиятро код пешакӣ санҷида наметавонад (ҳолати "аллакай фиристода шуд"/
    "7 рӯз гузашт" дар тарафи Meta аст) — `CommentAutomationJob` хатогии Meta-ро сабт
    мекунад, дубора кӯшиш НАМЕКУНАД (ниг. "Хатогиҳо ва retry" поён — сабаби АТАЙЯНА).

## Модели маълумот

Ду ҷадвали нав (`AddCommentAutomation`):

- **`automation_rules`**: `id, channel_id, name, is_active, trigger_type
  ("instagram_comment", string — на enum, то навъҳои нав бе миграция биёянд),
  trigger_config_json (jsonb), condition_config_json (jsonb, холӣ "{}" дар V1 — ҷои
  тасдиқи обуна дар оянда), action_config_json (jsonb), cooldown_minutes, created_at`.
- **`automation_runs`**: `id, rule_id, trigger_external_id (comment id),
  actor_external_id, target_media_external_id, matched_keyword,
  comment_reply_status, dm_status, error, created_at`.

**Иловаи аз спецификатсия берун:** `target_media_external_id`. Спецификатсия рӯйхати
сутунҳо дод, вале cooldown-и дархостшуда ("як actor дар як пост") бе ID-и пост
физикӣ ғайриимкон буд — актор метавонад дар якчанд пости гуногуни ҳамон канал
коментарий гузорад. Илова карда шуд бо ин сабаб, санҷида зинда (поён).

Config-ҳо ҳамчун typed record ҷудо нигоҳ дошта мешаванд
(`Channels/Automation/AutomationConfigs.cs`: `AutomationTriggerConfig`,
`AutomationActionConfig`), сериализатсия/дессериализатсия дар нуқтаи истифода — вақте
конструктори визуалӣ меояд, танҳо UI иваз мешавад, модел не.

## Мантиқи иҷро (pure, тестшаванда бе HTTP)

- `CommentAutomationMatcher.Match` — contains (ҳарфи калон/хурд бетафовут), `matchMode`
  (`keyword`/`all`), `postScope` (`all`/`selected` бо `postIds`). Ҳамин синф якборагӣ ҳам
  аз webhook-processing ва ҳам аз endpoint-и dry-run истифода мешавад — то мантиқ ду ҷо
  нусхабардорӣ нашавад ва dry-run ҳамеша натиҷаи production-ро диҳад.
- `CommentReplySelector.Select` — round-robin (на тасодуфӣ, интихоби қасдан: детерминистӣ,
  санҷиданаш осон). Индекс = шумораи run-ҳои қаблии ҳамин rule, ки `CreatedAt` пеш аз
  run-и ҷорӣ доранд — на сутуни иловагӣ, `CommentAutomationJob` онро дар лаҳзаи иҷро
  мешуморад.
- `CommentAutomationProcessor.ProcessAsync` (дар `WebhookProcessor.ProcessInternalAsync`,
  танҳо барои `ChannelType.Instagram` бо `entry[].changes[].field=="comments"` — ба ҷои
  шохаи муқаррарии `entry[].messaging[]`):
  1. Филтри ҳалқа: `evt.ActorExternalId == channel.ExternalId` → бозгашт, БЕ сабт (ин
     ҳатто run нест).
  2. Аввалин rule-и фаъоли мувофиқ (аз рӯи тартиби эҷод) — на ҳама rule-и мувофиқ, то ду
     ҷавоб ба як коментарий нафиристем.
  3. Cooldown: `AutomationRuns` бо ҳамин `(RuleId, ActorExternalId, TargetMediaExternalId)`,
     охирин сабт дар доираи `CooldownMinutes` бошад → сабти `SkippedCooldown`, бозгашт (rule-и
     дигар САНҶИДА НАМЕШАВАД — соддагии қасдани V1).
  4. Мувофиқ + бе cooldown → `AutomationRun(Pending)` сабт, `CommentAutomationJob.RunAsync`
     enqueue (Hangfire).

## Graph API (`InstagramProvider.cs`)

Се методи нав, `IChannelProvider` (интерфейси умумии WhatsApp/FB/IG) тағйир НАЁфт —
comment automation Instagram-хос аст (V1), job/endpoint онҳоро мустақим аз DI мегиранд:
- `ReplyToCommentAsync` — `POST /{comment-id}/replies`.
- `SendPrivateReplyAsync` — `POST /{ig-id}/messages` бо `recipient.comment_id`.
- `GetRecentMediaAsync` — `GET /{ig-id}/media` (пагинатсия бо cursor, VIDEO → fallback ба
  `thumbnail_url` дар backend, то frontend лозим набошад ду майдонро фарқ кунад).

Ҳама аз `PostToAbsoluteGraphApiPathAsync` (рефакторинги хурди `PostToGraphApiAsync`-и
Фазаи 7 — акнун ду overload: як бо `{accountId}/{path}`, як бо path-и мутлақ барои
`{comment-id}/replies`, ки ба account id-и худи мо асос наёфтааст) истифода мебаранд —
хатогиҳои 190 (токен)/rate-limit ройгон меоянд.

## Обунаи webhook

`InstagramOAuthConnector.RequiredWebhookFields` иваз шуд аз `["messages"]` ба
`["messages", "comments"]`. Азбаски ин обуна танҳо ҳангоми `/connect` фиристода мешавад,
каналҳои ПЕШ аз ин пайвастшуда обунаи "comments" надоштанд — ҳалли V1: ҳангоми сохтани
**аввалин rule-и фаъол** барои канал, `CommentAutomationEndpoints.CreateAsync` ин методро
бори дигар (идемпотентӣ) даъват мекунад.

## Хатогиҳо ва retry (қасдан ғайримуқаррарӣ)

`CommentAutomationJob` дорад `[AutomaticRetry(Attempts = 3, DelaysInSeconds = [30, 300,
1800])]` (ҳамон `MediaSendJob`), вале ҷавоби ҷамъиятӣ ва DM ҲАР КАДОМ дар catch-и ХУДАШ
сабт мешавад, ба берун партофта НАМЕШАВАД. Сабаб: агар ҷавоби ҷамъиятӣ муваффақ шуд, вале
DM ноком (масалан аз сабаби 7-рӯза гузашта), партофтани истисно [AutomaticRetry]-ро водор
мекард кӯшиши дуюм кунад — ки ҷавоби ҷамъиятии АЛЛАКАЙ фиристодашударо ТАКРОР мефиристод.
[AutomaticRetry] танҳо барои хатогиҳои беруни ин ду catch (DB/JSON вайрон) боқӣ мемонад.

## Гардиши ҷавобҳо ва dry run

`commentReplies[]` — round-robin (боло). Endpoint-и `POST .../automation-rules/dry-run`
stateless аст: trigger_config-и ҳанӯз захира НАШУДАи форма мегирад (на ruleId), то қоидаи
дар мобайни таҳрир низ санҷида шавад; ҳеҷ чиз ба DB сабт ё ба Meta фиристода намешавад
(санҷидашуда — ниг. поён).

## Интихоби пост (`GET /{id}/instagram-media`)

`InstagramMediaListResult` бо `IMemoryCache` (5 дақ, калид `channelId+after+limit`).
Frontend: grid 3-сутуна дар `MediaPickerModal.tsx`, ду таб (Ҳама/Интихобшуда), "боз бор
кун" (cursor-based). `trigger_config.postIds[]` танҳо ID нигоҳ медорад — расмҳо аз
Instagram кашида мешаванд, дар база сабт намешаванд.

## Санҷиши зинда (аккаунти воқеии production, 2026-09-14)

Бо канали воқеии `eshonov.f1` (external_id `17841438754823969`, `channels.manage`-и
owner):

| Санҷиш | Натиҷа |
|---|---|
| `POST /automation-rules` → обунаи webhook бори дигар | `webhook_setup_warning = null` — ҳарду `messages`+`comments` тасдиқ шуданд |
| `POST /automation-rules/dry-run` бо матни дорои калима | `{"matched":true,"matchedKeyword":"нарх"}`, БЕ дархости Meta |
| `POST /automation-rules/dry-run` бо эмодзи-танҳо | `{"matched":false}` |
| Webhook-и синтаксисан имзошудаи воқеӣ (comment field, HMAC-SHA256 бо `Meta:Instagram:AppSecret`) | 200, канал ёфта шуд, rule мувофиқ шуд, `AutomationRun(Pending)` сабт, job фавран иҷро шуд |
| `ReplyToCommentAsync` бо comment id-и сохта (воқеӣ вуҷуд надорад) | Meta 400: `code:100, subcode:33 "Object with ID '...' does not exist"` — далели возеҳ, ки эндпоинт/auth дуруст аст, танҳо comment id воқеӣ нест |
| `SendPrivateReplyAsync` ҳамон шарт | Meta 400: `code:100, subcode:2534014` ("корбари дархостшуда ёфт нашуд") — ҳамон далел |
| Филтри ҳалқа (`from.id == channel.ExternalId`) | 0 сатр дар `automation_runs` — партофта шуд, ҳатто сабт нашуд |
| Cooldown (60 дақ, ҳамон actor+пост) | Коментарии дуюм → `SkippedCooldown` дар ҳарду майдон |
| `PATCH .../active` (Disable) | 204 |

Хатогиҳои Meta дар боло **интизоршуда** буданд (comment id-и синтетикӣ вуҷуд надорад) —
далели муҳим ин аст, ки дархостҳо БО ФОРМАТИ ДУРУСТ ба Meta расиданд ва ҷавоб гирифтанд
(на хатои шабака/auth), ва `CommentAutomationJob` онҳоро дуруст, мустақилона сабт кард
(`comment_reply_status=Failed`, `dm_status=Failed`, `error` бо матни хонохои `MetaErrorTranslator`).
Санҷиши воқеии "ҷавоби ҳақиқӣ ба коментарии ҳақиқӣ" талаб мекунад коментарии зиндаро
интизор шудан — берун аз доираи ин сессия.

## Вазифаҳо

- [x] 10.1 Ҷадвалҳои `automation_rules`/`automation_runs` (миграция, EF configuration)
- [x] 10.2 `InstagramPayloadParser.TryParseCommentEvent` + шохаи нав дар `WebhookProcessor`
- [x] 10.3 `CommentAutomationMatcher`/`CommentReplySelector`/`CommentAutomationProcessor` (pure/тестшаванда)
- [x] 10.4 `InstagramProvider.ReplyToCommentAsync`/`SendPrivateReplyAsync`/`GetRecentMediaAsync`
- [x] 10.5 `CommentAutomationJob` (Hangfire, retry-и маҳдуд — ниг. боло барои сабаб)
- [x] 10.6 `CommentAutomationEndpoints` — CRUD, `PATCH .../active`, `POST .../dry-run`, `GET .../instagram-media`
- [x] 10.7 Обунаи webhook: "comments" илова, resubscribe ҳангоми аввалин rule
- [x] 10.8 Frontend: саҳифаи `/instagram-automation`, форма (ChipInput, MediaPickerModal, DryRunPanel)
- [x] 10.9 Тестҳо: keyword matching, филтри ҳалқа, cooldown, payload-и вайрон, канали ёфтнашуда

## Definition of Done

- ✅ Backend: 521 тест сабз (`dotnet test`), build бе хатогӣ/огоҳӣ
- ✅ Frontend: 479 тест сабз (`npm run test`), `npm run typecheck`, `npm run lint` (0 хато), `npm run build`
- ✅ Миграция дар DB-и воқеии dev санҷида шуд (`\d automation_rules`/`automation_runs`)
- ✅ Webhook → DB → Hangfire job → Graph API-и воқеӣ — занҷираи пурра бо аккаунти воқеӣ
  санҷида шуд (на mock)
- ✅ Dry-run ҳеҷ дархости берунӣ намекунад (санҷидашуда бо curl)
- ✅ Ҳимояҳои ҳатмии V1 (филтри ҳалқа, cooldown, хатогиҳои сабтшуда бе retry-и беназорат) —
  ҳар се бо аккаунти воқеӣ тасдиқ шуданд
