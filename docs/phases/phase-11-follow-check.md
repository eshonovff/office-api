# Фазаи 11 — Тасдиқи обуна дар автоматизатсияи коментарии Instagram (V2)

**Ҳадаф:** пеш аз ҷавоб додан ба коментарий, тафтиш кардан ки муаллиф ба
аккаунти бизнес обуна ҳаст ё не, ва вобаста ба натиҷа яке аз ду шохаи
ҷавоб (`onMatch`/`onNotFollowing`) фиристодан.
**Пешшарт:** Фазаи 10 (автоматизатсияи коментарии V1) ✅

> ✅ **2026-09-14: пурра иҷро шуд ва бо аккаунти воқеии production санҷида
> шуд** (follow-check-и воқеӣ, кэш, филиали onMatch/onNotFollowing).

## Далели тасдиқшуда (бо аккаунти воқеӣ, 2026-09-14)

`GET https://graph.instagram.com/v23.0/{user-id}?fields=username,is_user_follow_business`
бо токени канал → `{"followCheckResult":"Following"}` барои корбари воқеие,
ки ба мо паём фиристода буд (ID аз `webhook_logs`-и воқеӣ гирифта шуд).
`value.from.id`-и webhook-и коментарий мустақиман ҳамчун `user-id` кор кард —
табдил лозим нашуд. **Host: graph.instagram.com, на graph.facebook.com**
(ҳамон қоидаи Фазаи 10 — токенҳои IGAB... дар graph.facebook.com 190 медиҳанд).

## Тағйироти шикананда: action_config_json

`action_config_json`-и Фазаи 10 шакли ҳамвор дошт
(`{CommentReplies, DmText, DmButtonUrl, DmButtonTitle}`). Дар DB-и воқеӣ
аллакай як rule ("Faridun") бо ҳамин шакл вуҷуд дошт. Миграцияи
`AddFollowCheckCondition` онро **табдил** дод (на танҳо сутуни нав илова
кард):
```sql
UPDATE automation_rules
SET action_config_json = jsonb_build_object('OnMatch', action_config_json::jsonb, 'OnNotFollowing', NULL)
WHERE NOT (action_config_json::jsonb ? 'OnMatch');
```
Санҷидашуда дар DB-и dev: `action_config_json` баъд аз миграция —
`{"OnMatch": {...матни кӯҳна...}, "OnNotFollowing": null}`. `condition_config_json`
("{}" -и Фазаи 10-и ҳамаи rule-ҳо) ниёз ба табдил надошт —
`AutomationConditionConfig(bool RequiresFollow = false)` (default-и параметри
record дар C#) онро худкор `false` тафсир мекунад.

## Модел ва мантиқ

- `AutomationConditionConfig(RequiresFollow)`, `AutomationReplyAction
  (CommentReplies, DmText, DmButtonUrl, DmButtonTitle)`,
  `AutomationActionConfig(OnMatch, OnNotFollowing?)` —
  `Channels/Automation/AutomationConfigs.cs`.
- `AutomationBranchSelector.Select` (pure, 5 тест): `NotFollowing`+
  `OnNotFollowing` мавҷуд → `OnNotFollowing`; ҳама ҳолати дигар (`Following`,
  `Unknown`, `null`, ё `NotFollowing` бе `OnNotFollowing`) → `OnMatch`
  (fail-open — муштарӣ ҳеҷ гоҳ бе ҷавоб намемонад).
- Тафтиш дар **`CommentAutomationJob`** (на дар `CommentAutomationProcessor` —
  ҷудокунии Фазаи 10 нигоҳ дошта шуд: Processor = DB/мувофиқат/cooldown, бе
  HTTP; Job = ҳама дархости Graph API).

## `InstagramProvider.CheckFollowStatusAsync`

- **Ҳеҷ гоҳ истисно намепартояд** — хатогии HTTP/JSON/токен ҳама ба
  `Unknown` мераванд (`logger.LogWarning`, на Error — ин ҳолати муқаррарӣ
  аст). 190 → `channel.RequiresReconnect = true` (ҳамон алгуи Фазаи 10).
- **Кэш**: `IMemoryCache`, калиди `ig-follow:{channelId}:{actorId}`, 15
  дақ — ҳам барои натиҷаи муваффақ, ҳам Unknown.

**Санҷиши воқеии кэш** (curl, канали воқеӣ): дархости аввал — 0.85 сония
(бо HTTP-и воқеӣ ба Meta, ду сатри лог "Start processing"/"Sending HTTP
request"); дархости дуюм (ҳамон actor, дар давоми 15 дақ) — **0.01 сония**,
**СИФР сатри логи нав** барои ин URL — тасдиқи мустақими он, ки кэш дархости
такрориро пешгирӣ мекунад (ҳимояи хатари 200/соат-и спека).

## Санҷиши се ҳолат (воқеӣ)

| Ҳолат | Дархост | Натиҷа |
|---|---|---|
| Обуна ҳаст | `actorId` воқеӣ (аз `webhook_logs`) | `Following` — тасдиқшуда, `is_user_follow_business:true` |
| API хато медиҳад | `actorId` синтетикӣ (вуҷуд надорад) | `Unknown` — fail-open, ҳеҷ истисно, `AutomationRunStatus` идома ёфт |
| Обуна нест | — | Санҷиши воқеӣ карда нашуд (ID-и корбари ғайри-обунашуда дастрас набуд), вале роҳи коди ҳамон (`is_user_follow_business:false` → `NotFollowing`) ва интихоби шоха бо 5 тести pure (`AutomationBranchSelectorTests`) пурра пӯшонида шудааст |

Занҷири пурраи webhook → rule match → follow-check (воқеӣ, `Following`) →
`AutomationBranchSelector` (OnMatch) → `automation_runs.follow_check_result`
низ бо канали воқеӣ санҷида шуд (comment id-и синтетикӣ — хатои Meta ҳамон
"object does not exist"-и Фазаи 10, интизоршуда).

## Dry run

`POST .../automation-rules/dry-run` акнун follow-check-ро **воқеан** иҷро
мекунад (агар `conditionConfig.requiresFollow` ва `actorExternalId` дода
шуда бошанд) — хонданӣ, кэшдор, ҳеҷ чиз ба Meta фиристода намешавад.
Санҷидашуда: калима мувофиқ + `actorId`-и воқеӣ → `{"matched":true,
"followCheckResult":"Following"}`.

## UI

Бахши нав "Шарт" дар `RuleFormModal`: чекбокси "Танҳо барои обунашудагон".
Хомӯш — як блоки ҷавоб (ҳамон намуди V1). Фаъол — блоки дуюм "Ҷавоб барои
обунанашудагон" (танҳо commentReplies[]+dmText, БЕ тугма — тибқи спека).
`DryRunPanel` майдони нави ихтиёрии "Instagram user ID" мегирад, натиҷаи
шохаро нишон медиҳад.

## Ҳисобот

- **Вақти воқеии дархости тафтиш**: ~0.2-0.85 сония (бе кэш, вобаста ба
  шабака), **0.01 сония** (бо кэш).
- **Дархост дар як коментарий**: 1 (агар `requiresFollow=true` ва actor
  дар кэши 15-дақ набошад), 0 (агар дар кэш бошад ё `requiresFollow=false`).
- **Хатари 200/соат**: бо кэши 15-дақ, як actor ҳадди аксар 4 дархост дар
  як соат сарф мекунад (на ҳар коментарий). Барои аккаунти хурд/миёна
  (то садҳо коментарии беназир дар як соат) хатар кам аст; барои ҳаҷми
  хеле баланд (садҳо actor-и БЕНАЗИР дар як соат), лимити 200/соат метавонад
  расад — дар ин ҳолат бояд кэши дарозтар (масалан 1 соат) баррасӣ шавад.

## Навсозӣ 2026-09-15: ду масъалаи ёфтшуда бо санҷиши воқеии корбар

**А1 — DM ҳатмист буд, дар ҳоле ки корбар танҳо ҷавоби ҷамъиятӣ мехост.**
`DmText` акнун ихтиёрӣ аст — агар холӣ бошад, `CommentAutomationJob`
`SendPrivateReplyAsync`-ро тамоман НАМЕХОНАД, `DmStatus = Disabled` сабт
мешавад (аз `Failed` фарқ мекунад — ин хатогӣ нест, интихоби қасдонаи корбар
аст). Санҷидашуда зинда: `dm_status=Disabled`, `error` танҳо ҷавоби
ҷамъиятиро дар бар мегирад (DM ҳеҷ ҳиссагузорӣ намекунад).

**А2 — NotFollowing-и кэшшуда корбареро, ки ҳозир обуна шудааст, ҷазо
медиҳад.** Корбар худаш ин ҳолатро зинда ёфт: то обуна нашуда буд →
DM гуфт "обуна нестед"; баъд обуна шуд ва боз коментарий гузошт (дар
доираи ҳамон 15 дақ) → DM БОЗ ҳам "обуна нестед" гуфт, чунки кэш натиҷаи
кӯҳнаро баргардонд. Ҳал: **NotFollowing ҳеҷ гоҳ кэш намешавад** —
`CheckFollowStatusAsync` ҳар бор барои ин ҳолат бевосита ба Meta дархост
мезанад. Following/Unknown ҳамоно кэш мешаванд (ҳеҷ кас аз натиҷаи кӯҳнаи
онҳо зарар намебинад). Ба ҷои кэши NotFollowing, квота бо ду роҳи дигар
муҳофизат мешавад:
1. **Идемпотентии comment_id** (`CommentAutomationProcessor`) — агар
   webhook-и ҳамон коментарий такрор расад (Meta баъзан чунин мекунад),
   дубора коркард НАМЕШАВАД — санҷидашуда зинда: ду webhook-и айнан якхела
   → як сатри `automation_runs`.
2. **Буҷаи соатии 80%** (`InstagramFollowCheckRateLimiter`, 160 аз 200/соат-и
   Meta) — агар аз ин боло равад, дархости нав НАМЕРАВАД, `Unknown`
   бармегардад (fail-open, ҳамон рафтори хатои воқеӣ).

Тестҳо: `InstagramFollowCheckRateLimiterTests` (5), `InstagramProviderFollowCheckTests`
(6, бо `HttpMessageHandler`-и сохта — Following/Unknown кэш мешаванд,
NotFollowing не, буҷа), `ProcessAsync_CommentAlreadyProcessed_...` (1).

## Definition of Done

- ✅ Backend: 526 тест сабз (5 нав), build бе хатогӣ
- ✅ Frontend: 491 тест сабз (16 нав), typecheck/lint/build тоза
- ✅ Миграция дар DB-и воқеии dev санҷида шуд — rule-и мавҷуда ("Faridun")
  бе вайрон шудан табдил ёфт
- ✅ Follow-check-и воқеӣ (Following+Unknown бо аккаунти зинда, NotFollowing
  бо тестҳои pure) ва кэш (0 дархости такрорӣ дар 15 дақ) тасдиқ шуданд
- ✅ Занҷири пурраи webhook→job→follow-check→branch санҷида шуд
