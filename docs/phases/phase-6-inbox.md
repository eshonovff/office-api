# Фазаи 6 — Инбокс

**Ҳадаф:** API-и пурраи инбокс — рӯйхат, ҷавоб, статус, таъин, шаблон.
**Пешшарт:** Фазаи 5 ✅
**Тахмин:** 4 рӯз.

> ⚠️ **2026-08-09: татбиқи қисмӣ.** Корбар бевосита рӯйхати маҳдуди
> endpoint-ҳоро дархост кард (на ҳамаи 6.1-6.18). Иҷрошуда: 6.1, 6.3,
> 6.4 (бо саҳифабандии `page`/`pageSize`, на cursor — ниг. эзоҳи 6.2),
> 6.6, 6.8+6.9 (як endpoint-и якҷояи PATCH, на ду ҷудогона), 6.17, 6.18.
> Иҷронашуда: 6.2 (cursor), 6.5 (board), 6.7 (notes), 6.10 (read),
> 6.11 (tags), 6.12-6.14 (филтри доступ — channel_members/only_assigned),
> 6.15-6.16 (CRUD-и шаблон — фиристодани шаблон дар 6.6 кор мекунад,
> вале рӯйхати идоракунӣ нест). DoD-и пурра ҳанӯз тасдиқ НАШУДААСТ.

## Дарун
Query-ҳои чат ва паём, статус, таъин бо DnD, ёддошти дохилӣ, тег, шаблон, доступ.

## Берун
Автоҷавоб, чатбот, AI — **нест**. Ҳисобот — нест.

## Вазифаҳо

### Хондан
- [x] 6.1 `GET /api/conversations` — филтр: `channelId`, `status`, `assignedTo` (`unread`/`tag`/`q` — не, дархост нашуда буд)
- [ ] 6.2 Тартиб: `last_message_at DESC`; cursor pagination — тартиб иҷрошуда, вале саҳифабандӣ `page`/`pageSize` (offset), на cursor
- [x] 6.3 `GET /api/conversations/{id}`
- [x] 6.4 `GET /api/conversations/{id}/messages` — аз нав ба кӯҳна, саҳифабандӣшуда (offset, на cursor)
- [ ] 6.5 `GET /api/conversations/board` — гурӯҳбандӣ аз рӯи статус барои Kanban

### Амал
- [x] 6.6 `POST /api/conversations/{id}/messages` — ҷавоб (`inbox.reply`), матни озод/шаблон, 409 агар тиреза баста
- [ ] 6.7 `POST /api/conversations/{id}/notes` — ёддошти дохилӣ, ба мижоз намеравад
- [x] 6.8 `PATCH /api/conversations/{id}/status` — амалӣ шуд ҳамчун қисми `PATCH /api/conversations/{id}` (не endpoint-и алоҳида), `inbox.close` барои `closed`
- [x] 6.9 `PATCH /api/conversations/{id}/assign` — амалӣ шуд ҳамчун қисми `PATCH /api/conversations/{id}` (не endpoint-и алоҳида)
- [ ] 6.10 `POST /api/conversations/{id}/read` — `unread_count = 0`
- [ ] 6.11 `PUT /api/conversations/{id}/tags`

### Доступ ⚠️
- [ ] 6.12 Филтри канал: танҳо каналҳое ки узви `channel_members` аст
- [ ] 6.13 Филтри `only_assigned`: танҳо чатҳои `assigned_to = me`
- [ ] 6.14 Ҳарду филтр дар **ҳамаи** query-ҳои боло — як ҷои умумӣ (extension method), на такрор

### Шаблон
- [ ] 6.15 CRUD-и `message_templates` (`templates.manage`)
- [ ] 6.16 `GET /api/templates?channelType=...` — барои autocomplete-и `/shortcut`

### Realtime
- [x] 6.17 Event: `MessageReceived`, `MessageSent`, `ConversationAssigned`, `ConversationStatusChanged`
- [x] 6.18 Ба гурӯҳи `channel:{id}` ва `user:{assignedTo}`

## Definition of Done

> ✅ 2026-08-09: `POST /api/conversations/{id}/messages` ва
> қабули паём (webhook → `MessageReceived`) дар сервер бо WhatsApp-и
> воқеӣ санҷида шуд — корбар тасдиқ кард. Банди зерин ба 6.12-6.14
> (филтри доступ) ва 6.7 (notes) вобастаанд, ки ҳанӯз сохта НАШУДААНД
> — новобаста аз санҷиши WhatsApp.

- ⬜ Operator танҳо каналҳои худро мебинад (6.12-6.14 иҷро нашуд)
- ⬜ Operator бо `only_assigned` танҳо чатҳои худро мебинад (6.12-6.14 иҷро нашуд)
- ✅ Тағйири статус ва таъин дар браузери дигар фавран намоён (`ConversationAssigned`/`ConversationStatusChanged` санҷида шуд)
- ⬜ Ёддошти дохилӣ ба WhatsApp намеравад (6.7 иҷро нашуд)
- 🟡 Ҷавоб бо шаблон кор мекунад (`SendTemplateAsync` васл шуд; санҷиши WhatsApp-и воқеӣ фиристодан/қабулро тасдиқ кард, вале мушаххас "шаблон дар тирезаи баста" алоҳида санҷида нашуд)
