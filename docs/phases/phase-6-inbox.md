# Фазаи 6 — Инбокс

**Ҳадаф:** API-и пурраи инбокс — рӯйхат, ҷавоб, статус, таъин, шаблон.
**Пешшарт:** Фазаи 5 ✅
**Тахмин:** 4 рӯз.

## Дарун
Query-ҳои чат ва паём, статус, таъин бо DnD, ёддошти дохилӣ, тег, шаблон, доступ.

## Берун
Автоҷавоб, чатбот, AI — **нест**. Ҳисобот — нест.

## Вазифаҳо

### Хондан
- [ ] 6.1 `GET /api/conversations` — филтр: `channelId`, `status`, `assignedTo`, `unread`, `tag`, `q`
- [ ] 6.2 Тартиб: `last_message_at DESC`; cursor pagination
- [ ] 6.3 `GET /api/conversations/{id}`
- [ ] 6.4 `GET /api/conversations/{id}/messages` — cursor, 50-тоӣ, аз нав ба кӯҳна
- [ ] 6.5 `GET /api/conversations/board` — гурӯҳбандӣ аз рӯи статус барои Kanban

### Амал
- [ ] 6.6 `POST /api/conversations/{id}/messages` — ҷавоб (`inbox.reply`)
- [ ] 6.7 `POST /api/conversations/{id}/notes` — ёддошти дохилӣ, ба мижоз намеравад
- [ ] 6.8 `PATCH /api/conversations/{id}/status` — DnD дар Kanban (`inbox.close` барои `closed`)
- [ ] 6.9 `PATCH /api/conversations/{id}/assign` — DnD ба аватар (`inbox.assign`)
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
- [ ] 6.17 Event: `MessageReceived`, `MessageSent`, `ConversationAssigned`, `ConversationStatusChanged`
- [ ] 6.18 Ба гурӯҳи `channel:{id}` ва `user:{assignedTo}`

## Definition of Done
- Operator танҳо каналҳои худро мебинад
- Operator бо `only_assigned` танҳо чатҳои худро мебинад
- Тағйири статус ва таъин дар браузери дигар фавран намоён
- Ёддошти дохилӣ ба WhatsApp намеравад
- Ҷавоб бо шаблон кор мекунад
