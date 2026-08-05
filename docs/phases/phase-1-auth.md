# Фазаи 1 — Auth ва доступ

**Ҳадаф:** даромадан, сохтани корманд, додани доступ, санҷиши воқеии доступ.
**Пешшарт:** Фазаи 0 ✅
**Тахмин:** 5 рӯз.
**Ҳатман хон:** `docs/03-permissions.md`, `docs/04-data-model.md`

## Дарун
Entity-ҳои auth, JWT, refresh, permission engine, endpoint-ҳои `/auth`, `/users`, `/roles`.

## Берун
Проект, таск, канал, чат. Хабарҳо. Upload-и аватар (дертар).

## Вазифаҳо

### Модели маълумот
- [x] 1.1 Entity: `User`, `Role`, `UserRole`, `RolePermission`, `UserPermission`, `RefreshToken`
- [x] 1.2 `AppDbContext` — калидҳо, index, snake_case naming
- [x] 1.3 Migration `InitialAuth` + `database update`

### Permission engine
- [x] 1.4 `Auth/Permissions.cs` — константаҳо + `All` HashSet
- [x] 1.5 `PermissionService.ResolveAsync` — формулаи `docs/03`
- [x] 1.6 `PermissionService.BumpVersionAsync` — версия +1 ва revoke-и refresh
- [x] 1.7 `PermissionRequirement` + `PermissionAuthorizationHandler` (bypass барои `owner`)
- [x] 1.8 `PermissionPolicyProvider` — policy-и динамикӣ `perm:*`
- [x] 1.9 Extension `.RequirePermission("...")`
- [x] 1.10 **Тест:** роль ∪ иҷозат − манъ (3 кейс)

### Токен
- [x] 1.11 `TokenService.CreateAccessToken` — claim-ҳои `perm`, `role`, `pv`
- [x] 1.12 `TokenService.CreateRefreshToken` — раками тасодуфӣ, дар база танҳо SHA-256
- [x] 1.13 JWT Bearer дар `Program.cs`, `ClockSkew = TimeSpan.Zero`
- [x] 1.14 Middleware: муқоисаи `pv`-и токен бо база → номувофиқ 401

### Seed
- [x] 1.15 5 роли системавӣ бо permission-ҳояшон
- [x] 1.16 Корманди Owner аз User Secrets, `must_change_password = true`
- [x] 1.17 Seed idempotent — дубора иҷро шавад, дубликат насозад

### Endpoint-ҳои Auth
- [x] 1.18 `POST /api/auth/login` — ҳамон матни хато барои логин ва пароли нодуруст
- [x] 1.19 `POST /api/auth/refresh` — бо ротатсия (кӯҳна фавран revoke)
- [x] 1.20 `POST /api/auth/logout`
- [x] 1.21 `POST /api/auth/change-password` — мин. 8 аломат, баъд `BumpVersion`
- [x] 1.22 `GET /api/auth/me` — роль ва permission-ҳо
- [x] 1.23 Rate limit дар `/login` — 5 кӯшиш / дақиқа / IP

### Endpoint-ҳои Users
- [x] 1.24 `GET /api/users` — рӯйхат бо ролҳо, филтр ва ҷустуҷӯ
- [x] 1.25 `GET /api/users/{id}` — бо истиснопҳо
- [x] 1.26 `POST /api/users` — логин + пароли муваққатӣ
- [x] 1.27 `PATCH /api/users/{id}` — ном, телефон, `only_assigned`
- [x] 1.28 `PUT /api/users/{id}/roles` — массиви `roleId`, баъд `BumpVersion`
- [x] 1.29 `PUT /api/users/{id}/permissions` — истиснопҳо, баъд `BumpVersion`
- [x] 1.30 `POST /api/users/{id}/reset-password`
- [x] 1.31 `PATCH /api/users/{id}/active` — soft disable
- [x] 1.32 Ҳимоя: Owner-и охиринро ғайрифаъол кардан мумкин нест

### Endpoint-ҳои Roles
- [x] 1.33 `GET /api/roles`
- [x] 1.34 `GET /api/permissions` — рӯйхати ҳама калидҳо барои UI
- [x] 1.35 `POST /api/roles`, `PATCH /api/roles/{id}`
- [x] 1.36 `PUT /api/roles/{id}/permissions`
- [x] 1.37 `DELETE /api/roles/{id}` — `is_system` нест намешавад

## Definition of Done
- Юзери тестӣ бо ролҳои `developer` + `operator` сохта мешавад
- Аз Scalar login → `/auth/me` ҳарду маҷмӯи permission-ро якҷоя нишон медиҳад
- Endpoint-и `users.manage` ба ӯ **403** медиҳад
- Манъи шахсии `inbox.close` кор мекунад
- Баъди иваз кардани роль, токени кӯҳна **401** мегирад
- 5 кӯшиши нодурусти login → **429**
- Тестҳои 1.10 сабз
