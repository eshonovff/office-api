# Фазаи 2 — Проект ва таск

**Ҳадаф:** Kanban-и корӣ бо кӯчонидан ва таъин кардан аз тарафи API.
**Пешшарт:** Фазаи 1 ✅
**Тахмин:** 6 рӯз.

## Дарун
Проект, аъзо, колонка, таск, комментарий, файл, тег, таърих, `move`, `assign`.

## Берун
SignalR (фазаи 3). Sprint, story point, time tracking, тобеияти таскҳо — **нест**.

## Вазифаҳо

### Модел
- [ ] 2.1 Entity: `Project`, `ProjectMember`, `BoardColumn`, `TaskItem`, `TaskComment`, `TaskAttachment`, `Label`, `TaskLabel`, `TaskActivity`
- [ ] 2.2 Migration `Projects` + index-ҳои `docs/04`

### Проект
- [ ] 2.3 `GET /api/projects` — танҳо проектҳое ки узв аст (Owner/Admin ҳама)
- [ ] 2.4 `POST /api/projects` — 4 колонкаи пешфарз автоматӣ
- [ ] 2.5 `PATCH /api/projects/{id}`, `POST /api/projects/{id}/archive`
- [ ] 2.6 `PUT /api/projects/{id}/members`
- [ ] 2.7 Filter-и умумӣ: узв нест → **404**, на 403

### Колонка
- [ ] 2.8 `POST/PATCH/DELETE /api/projects/{id}/columns`
- [ ] 2.9 `PUT /api/projects/{id}/columns/order`
- [ ] 2.10 Колонкаи холинабуда нест намешавад → **409**

### Таск
- [ ] 2.11 `GET /api/projects/{id}/board` — колонкаҳо бо таскҳо, як query
- [ ] 2.12 `GET /api/tasks` — филтр: масъул, тег, приоритет, deadline, матн
- [ ] 2.13 `GET /api/tasks/{id}` — пурра
- [ ] 2.14 `POST /api/tasks` — `position` = охирини колонка + 1000
- [ ] 2.15 `PATCH /api/tasks/{id}`
- [ ] 2.16 `DELETE /api/tasks/{id}`

### Drag & drop ⚠️ ҷои муҳим
- [ ] 2.17 `PATCH /api/tasks/{id}/move` — вуруд: `columnId`, `beforeTaskId?`, `afterTaskId?`
- [ ] 2.18 Ҳисоби `position` = миёнаи ҳамсояҳо; холӣ → 1000; аввал → аввалин/2; охир → охирин+1000
- [ ] 2.19 Reindex-и колонка агар фарқи ҳамсояҳо < 0.0001
- [ ] 2.20 Тавассути `SELECT ... FOR UPDATE` ё transaction — то ду кӯчонидани ҳамзамон вайрон накунад
- [ ] 2.21 **Тест:** 6 кейси `position` (холӣ, аввал, миён, охир, ҳамсояи наздик, reindex)
- [ ] 2.22 `PATCH /api/tasks/{id}/assign` — `assigneeId` ё `null`; масъул бояд узви проект бошад

### Илова
- [ ] 2.23 CRUD-и комментарий + `@mention` (парсинг ва сабт)
- [ ] 2.24 Upload/download/delete-и файл; лимити 20 МБ; навъҳои иҷозатдодашуда
- [ ] 2.25 CRUD-и тег дар доираи проект
- [ ] 2.26 `TaskActivity` — сабти автоматии тағйирот
- [ ] 2.27 `GET /api/tasks/{id}/activity`

## Definition of Done
- Аз Scalar таск сохта, кӯчонда, таъин карда мешавад
- 20 таскро дар як колонка бо тартиби тасодуфӣ кӯчонӣ — тартиб вайрон намешавад
- Developer таскро кӯчонда метавонад, вале таъин карда наметавонад (`tasks.assign` надорад)
- Корманде ки узви проект нест → `GET /api/projects/{id}/board` = 404
- Тестҳои 2.21 сабз
