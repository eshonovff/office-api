# Фазаи 3 — Realtime

**Ҳадаф:** тағйирот дар ҳамаи браузерҳои кушода фавран намоён шавад.
**Пешшарт:** Фазаи 2 ✅
**Тахмин:** 2 рӯз.

## Дарун
SignalR hub-и board ва inbox, авторизатсия, event-ҳо, ҷадвали `notifications`.

## Берун
Push-и мобилӣ, email, огоҳии садоӣ (frontend).

## Вазифаҳо
- [x] 3.1 SignalR + конфигуратсия дар `Program.cs`
- [x] 3.2 Авторизатсияи hub бо ҳамон JWT (токен аз query string барои WebSocket)
- [x] 3.3 `BoardHub` — `/hubs/board`, гурӯҳ `project:{id}`
- [x] 3.4 Ҳангоми `OnConnected` — санҷиши узвият пеш аз ҳамроҳ кардан ба гурӯҳ
- [x] 3.5 Event: `TaskCreated`, `TaskMoved`, `TaskUpdated`, `TaskDeleted`, `CommentAdded`
- [x] 3.6 `InboxHub` — `/hubs/inbox`, гурӯҳҳо `user:{id}` ва `channel:{id}` (омода барои фазаи 6)
- [x] 3.7 Entity `Notification` + migration
- [x] 3.8 `NotificationService.PushAsync` — база + SignalR
- [x] 3.9 `GET /api/notifications`, `POST /api/notifications/read`
- [x] 3.10 Огоҳӣ ҳангоми: таск ба ту таъин шуд, дар комментарий @mention шудӣ, deadline фардо

## Definition of Done
- Ду браузер кушода — кӯчонидан дар яке фавран дар дуюм намоён
- Корманде ки узви проект нест, ба гурӯҳи `project:{id}` дохил намешавад
- Огоҳӣ ҳам дар база сабт мешавад, ҳам realtime мерасад
