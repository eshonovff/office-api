# Фазаи 12 — Flow Builder (конструктори визуалии автоматизация)

**Ҳадаф:** дар паҳлӯи автоматизатсияи оддии Фазаи 10/11 (як триггер+шарт+амал),
навъи дуюм илова кардан — `flow`: граф аз нодҳо (паём/шарт/амал/қайд) бо
ҳолати доимии ҳар контакт дар дохили граф (`flow_sessions`), ва рӯйхати
ягонаи "Автоматизатсияҳо" барои ҳарду навъ.
**Пешшарт:** Фазаи 10 (V1) ✅, Фазаи 11 (follow-check) ✅

> ✅ **2026-09-15: backend пурра иҷро ва бо аккаунти воқеии production
> санҷида шуд. Frontend (canvas UI) низ ҳамон рӯз пурра иҷро шуд** —
> ниг. `office-web/docs/phases/phase-9-flow-builder-ui.md`. Ин ҳуҷҷат
> танҳо кисми backend-ро дар бар мегирад.

## Тасмимҳои тарроҳӣ

1. **Бе `tenant_id`** — система дар ҳеҷ ҷои дигар тенант надорад.
2. **Бе кӯчонидани физикии `automation_rules`** — ҷадвали кӯҳна
   (як rule-и зиндаи "Faridun" дар production) бетағйир монд.
   `GET /api/automations` рӯйхати ЯГОНАро аз ду сарчашма месозад
   (`automation_rules` → `type=simple`, `flows` → `type=flow`) дар сатҳи
   endpoint (union дар C#, на дар DB).
3. **`contact_id` = `Conversation.Id`** — тадқиқот тасдиқ кард, ки ҳеҷ
   ҷадвали "Contact"-и мустақил вуҷуд надорад. `contact_tags`/
   `contact_variables` ба `conversations(id)` FK мешаванд.
4. **`group_id` илова НАШУД** (YAGNI — спека худаш онро ихтиёрӣ гуфт).
5. **Тугмаи `action:"payment"`** дар схема қабул мешавад (мутобиқати
   JSON), вале дар backend рад мешавад (`FlowsEndpoints.ValidateNodeConfig`)
   — тибқи "НАГИР"-и спека.
6. **`FlowWaitReason`** (илова аз рӯи худи спека): `Delay | ButtonClick |
   CollectInput | WindowClosed` — бе ин, `ResumeFromMessageAsync` намедонист
   паёми нави корбар "ҷавоби collect_input" аст ё "тирезаи 24-соата кушода
   шуд, ҳамон нодро аз нав кӯшиш кун".
7. **`flow_session_steps`** (ҷадвали нав, на дар спека) — логи ҳар қадам
   барои омори кумулятивӣ ("чанд контакт ба ин нод расиданд").
8. **`InstagramProvider.SendButtonMessageAsync`** — методи нав ба
   `recipient.id` (на `comment_id`-и Фазаи 10). Facebook-и flow дар ин
   фаза дастгирӣ намешавад.
9. **Postback**: шакли webhook тасдиқ карда шуд, парсинги нав дар
   `InstagramPayloadParser.ParsePostback` илова шуд.
10. **Delay**: `IBackgroundJobClient.Schedule<FlowEngineJob>`,
    `flow_sessions.scheduled_job_id` нигоҳ дошта мешавад.
11. **Loop guard**: `flow_sessions.step_count`, ҳадди 50 (`FlowSessionLoopGuard`).
12. **`http_request` action**: SSRF-и оддӣ манъ шуд (`HttpRequestUrlGuard`)
    — танҳо `https://`, IP-ҳои маҳаллӣ/хусусӣ манъ.

## Модели маълумот (8 ҷадвали нав)

```
flows, flow_nodes, flow_edges, flow_sessions, flow_session_steps,
contact_tags, contact_variables, flow_templates
```

Миграция: `20260915070302_AddFlowBuilder`. Санҷидашуда дар DB-и dev:
`automation_rules` физикӣ бетаъсир монд (ҳамон 1 сатри "Faridun").

## Ҳаракатчии иҷро (`Channels/Flows/FlowEngine.cs`, 480 сатр)

- `StartAsync(flow, contactId, triggerExternalId?)`,
  `ResumeFromDelayAsync`, `ResumeFromButtonAsync(postbackPayload)`,
  `ResumeFromMessageAsync(sessionId, messageBody)` — ҳар кадом бар асоси
  `WaitReason` рафтори дурустро интихоб мекунад.
- `RunLoopAsync`: ҳар нод дар `try/catch`-и худ — агар як нод хато диҳад,
  **тамоми сессия** `Failed` мешавад (на танҳо як амал, баръакси
  `CommentAutomationJob`-и Фазаи 10 — идомаи граф баъди қадами ноком
  хатарнок аст, аз ин рӯ қасдан қатъӣ).
- Пеш аз ҳар фиристодани паём: `ConversationWindowCalculator.IsWindowClosed`
  (мавҷуда, бе тағйир) — агар баста → `status=Waiting,
  wait_reason=WindowClosed`, ҳамон нод дубора кӯшиш мекунад вақте паёми
  нави корбар (тирезаро мекушояд) расад.
- `ConditionNodeExecutor`: шарти `subscription` ҳамон
  `InstagramProvider.CheckFollowStatusAsync`-и Фазаи 11-ро истифода
  мебарад (ҳамон кэши 15-дақ), вале **фақат агар қоидаи subscription
  дар граф воқеан истифода шуда бошад** — на ҳар қадам.
- `AddTagsAsync`: багест ёфта шуд бо тести loop-guard — тег такроран дар
  як давраи иҷрои НОСАБТ (unsaved) илова шудан метавонист EF-ро бо
  "entity already tracked" бишиканад. Ҳал: санҷиши дубора ҳам аз DB, ҳам
  аз `db.ChangeTracker.Entries<ContactTag>()`.

## Webhook integration

`FlowTriggerProcessor` — паҳлӯи `CommentAutomationProcessor`-и мавҷуда
(на ба ҷои он): ҳар ду дар `WebhookProcessor` якҷоя даъват мешаванд.
Идемпотентӣ бо `comment_id`/`message_id` (ҳамон алгуи Фазаи 11).
Агар барои `contact_id` сессияи `Waiting` мавҷуд бошад → resume; вагарна
flow-и фаъоли мувофиқ ёфта, сессияи нав сар мешавад.

## Endpoints (11 нав)

- `Features/Flows/FlowsEndpoints.cs` (8): CRUD-и flow + `PUT
  /api/flows/{id}/graph` (иваз кардани пурраи nodes+edges якҷоя, барои
  autosave-и оддӣ — на N дархости алоҳида) + `PATCH .../active` +
  `GET .../stats` (аз `flow_session_steps` GROUP BY).
- `Features/Flows/FlowTemplatesEndpoints.cs` (2): рӯйхат + нусхабардорӣ
  (`POST /api/channels/{channelId}/flows/from-template/{templateId}`).
- `Features/Automations/AutomationsEndpoints.cs` (1): `GET
  /api/automations?channelId=&search=&sort=` — union.

Ҳама бо `RequirePermission(Permissions.Channels.Manage)`.

## 3 шаблони аввалия (`FlowTemplateSeeder`, идемпотентӣ)

1. **"Лид-магнит бо тасдиқи обуна"** — шарт→шоха, тугмаи такрории
   "Ман обуна шудам" ба худи шарт бармегардад.
2. **"Ҷавоб ба комментарий + DM"** — як нодаи паём (баробари V1).
3. **"Ҷамъоварии контакт"** — 6 нод (ном→collect→телефон→collect→тег→ташаккур).

## Санҷиши зиндаи пурра (2026-09-15, канали воқеӣ `eshonov.f1`)

Шаблони "Ҷамъоварии контакт" нусхабардорӣ шуд ба flow-и воқеӣ (триггери
DM бо калимаи "контакт"), баъд webhook-и воқеии имзошуда (HMAC-SHA256)
бо ID-и корбари воқеии обунашуда (`1366210167906580`, ҳамон корбари
тасдиқшудаи Фазаи 11) фиристода шуд:

| Санҷиш | Натиҷа |
|---|---|
| Actor синтетикӣ (вуҷуд надорад) | Meta 400, code 100, "объект вуҷуд надорад" — тасдиқи дурустии шакли payload, на нокомии шабака |
| Actor воқеӣ, паёми "контакт" | Webhook → trigger match → сессияи нав → нодаи 1 (паём "Салом! Номи шумо чист?") **воқеан фиристода шуд** (Meta 200 OK, 0.81с) → сессия дуруст ба нодаи 2 (`collect_input`, `status=Waiting, wait_reason=CollectInput, step_count=2`) гузашт |

Ин занҷири пурраи аввалини **webhook → FlowTriggerProcessor → FlowEngine
→ Graph API воқеӣ → ҳолати дурусти waiting** аст, бе ягон хатогӣ дар лог.
Тамоми маълумоти санҷишӣ (`flow_sessions`/`flow_session_steps`/flow-и
тестӣ) баъд аз санҷиш тоза карда шуд; `automation_rules` ва contact-ҳои
воқеӣ бетаъсир монданд.

## Ҳисобот

- **8 ҷадвали нав**, **11 endpoint-и нав**, **480 сатр** `FlowEngine.cs`.
- **46 тести нави pure/integration** (loop guard, interpolation, condition
  evaluator, SSRF guard, engine — старт/шоха/тугма/delay/collect-input/
  window-closed/goto_flow/http_request, trigger processor) — ҳамагӣ
  **598 тести backend сабзанд**, build бе хатогӣ.
- **Вақти воқеии як қадами паём**: ~0.8 сония (латентии Graph API-и Meta,
  мутобиқи Фазаҳои 10/11 — худи `FlowEngine` овери иловагӣ намеафзояд).
- **Бори иловагӣ ба Hangfire**: як job барои ҳар `delay`-и flow
  (`FlowEngineJob`, `[AutomaticRetry(3, 30/300/1800с)]`) — ҳамон алгуи
  `CommentAutomationJob`-и мавҷуда, навбати алоҳида илова НАШУД (бори
  кутоҳмуддат, дар ҳамон навбати умумӣ кофист).
- **automation_rules бетаъсир монд** — на сохтор, на маълумот тағйир наёфт.

## Чӣ иҷро нашуд

Frontend ҳамон рӯз (2026-09-15) пурра иҷро шуд — ниг.
`office-web/docs/phases/phase-9-flow-builder-ui.md`. Танҳо як чиз
боқӣ монд: **санҷиши дастии зиндаи browser** (drag-и нод, autosave-и
воқеӣ, reload+тасдиқи сабтшавӣ) — дар муҳити он сессия абзори browser
automation дастрас набуд, тамоми санҷиш `typecheck`/`test`/`lint`/
`build` буд. Тавсия: пеш аз production, як бор дастӣ санҷида шавад.

Дар доираи кори frontend, ду майдони хурд ба backend илова шуд
(`FlowListItem`/`FlowDetail.ChannelId`) — canvas-ро лозим буд, ҳеҷ
endpoint-и мавҷударо нашикаст.

## Definition of Done (backend)

- ✅ Миграция дар DB-и воқеии dev санҷида шуд, `automation_rules` бетаъсир
- ✅ 598 тести backend сабз, build бе хатогӣ
- ✅ Занҷири пурраи webhook→trigger→session→engine→Graph API воқеӣ санҷида шуд
- ✅ Postback ва button-message воқеан ба Instagram фиристода шуданд
- ✅ Frontend (canvas) — пурра иҷро шуд, ниг. `office-web/docs/phases/phase-9-flow-builder-ui.md`
- ⬜ Санҷиши дастии зиндаи browser (frontend) — тавсияшуда, иҷро нашуд
