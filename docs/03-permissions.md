# 03 — Модели доступ

## Формула

```
Доступи ниҳоӣ = (∪ permission-ҳои ҳамаи ролҳои корбар)
              + user_permissions бо is_granted = true
              − user_permissions бо is_granted = false
```

**Манъ ҳамеша болотар аз иҷозат.**
**Owner санҷишро тамоман мегузарад** (bypass дар handler).

## Се сатҳ

1. **Роль** — асос. Як корбар **якчанд роль** дошта метавонад (`user_roles` — many-to-many).
2. **Иловаи шахсӣ** — `is_granted = true`. «Ин dev таск таъин карда тавонад» бе сохтани роли нав.
3. **Манъи шахсӣ** — `is_granted = false`. «Оператор аст, вале чат пӯшида наметавонад.»

## Рӯйхати ниҳоии permission-ҳо

```
users.view          users.manage
roles.view          roles.manage
projects.view       projects.manage
tasks.view          tasks.create      tasks.edit
tasks.delete        tasks.assign      tasks.move
inbox.view          inbox.reply       inbox.assign
inbox.close         inbox.delete
channels.manage     templates.manage
```

Дар `Auth/Permissions.cs` ҳамчун константа. **Калиди нав танҳо баъди тасдиқ.**

## Ролҳои системавӣ (seed, `is_system = true`)

| Роль | Permission-ҳо |
|---|---|
| `owner` | ҳама (bypass) |
| `admin` | ҳама ғайр аз `users.manage`, `roles.manage` |
| `developer` | `projects.view`, `tasks.view/create/edit/move` |
| `operator` | `inbox.view/reply/close` |
| `manager` | `projects.view`, `tasks.view/create/assign`, `inbox.view/reply/assign/close`, `templates.manage` |

## Сатҳи дуюм — доступ ба объект

Permission иҷозати умумӣ медиҳад. Доступ ба объекти мушаххас алоҳида:

| Ҷадвал | Маъно |
|---|---|
| `project_members` | кадом проектҳоро мебинад |
| `channel_members` | кадом каналҳоро мебинад |
| `users.only_assigned` | танҳо таск/чати ба худаш додашуда |

**Ҳар query-и рӯйхат бояд ин филтрро дошта бошад.** Ин ҷои маъмултарини хатогист.

## Токен

- Permission-ҳо ҳамчун claim-и `perm` дар JWT — то ҳар request ба база наравад
- `pv` (permissions_version) дар токен
- Ҳангоми тағйири роль/доступ: `users.permissions_version + 1` ва ҳамаи refresh token-ҳо revoke
- Middleware `pv`-и токенро бо база муқоиса мекунад — номувофиқ → 401

## Дар код

```csharp
app.MapPost("/api/tasks", handler)
   .RequirePermission(Permissions.Tasks.Create);
```

Дар frontend танҳо тугмаро пинҳон кун — **санҷиши воқеӣ ҳамеша дар backend.**
