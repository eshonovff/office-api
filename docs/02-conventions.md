# 02 — Конвенсияҳо

## Ном

| Чиз | Услуб | Мисол |
|---|---|---|
| Class, record, метод | PascalCase | `TaskItem`, `CreateUser` |
| Майдони private | `_camelCase` | `_db` |
| Ҷадвал ва сутун | snake_case | `user_roles`, `created_at` |
| Endpoint | kebab-case | `/api/conversations/{id}/mark-read` |
| Permission | `domain.action` | `tasks.assign` |
| Ветка | `feat/phase-N-name` | `feat/phase-2-tasks` |

## API

- Prefix: `/api`
- Гурӯҳбандӣ: `app.MapGroup("/api/tasks").WithTags("Tasks")`
- Хатогӣ: ҳамеша `ProblemDetails`, матни `detail` бо забони тоҷикӣ
- Санаҳо: `DateTimeOffset`, ҳамеша **UTC**, формати ISO-8601
- ID: `Guid` бо `Guid.CreateVersion7()` — тартибдор, index-и хуб
- Pagination: cursor-based (`?cursor=...&limit=50`), на offset

### Кодҳои ҳолат

| Код | Кай |
|---|---|
| 200 | Хондан муваффақ |
| 201 | Сохта шуд + `Location` |
| 204 | Тағйир/нест кардан муваффақ |
| 400 | Валидатсия |
| 401 | Токен нест ё тамом |
| 403 | Токен ҳаст, доступ нест |
| 404 | Нест — **ё доступ ба объект нест** |
| 409 | Ихтилоф (username банд, position ишғол) |

**Қоида:** агар корбар ба объект доступ надошта бошад, `404` деҳ, на `403` — то мавҷудияти объект ошкор нашавад.

## Валидатсия

FluentValidation, як validator дар як папкаи feature, ба таври автоматӣ бо filter.

## Log

```csharp
_logger.LogInformation("Task {TaskId} moved to column {ColumnId} by {UserId}", ...);
```
Ҳеҷ гоҳ парол, токен, credentials-и канал дар log.

## Commit

```
feat(auth): add refresh token rotation
fix(tasks): correct position calculation on drop
chore(deps): bump EF Core to 10.0.1
docs(phase-2): mark task 2.6 done
```

## Тест

Ҳозир unit test-и пурра лозим нест. Вале ин ду ҷо **ҳатман** тест дошта бошанд:
- Ҳисоби `EffectivePermissions` (роль ∪ иҷозат − манъ)
- Ҳисоби `position` ҳангоми drag & drop
