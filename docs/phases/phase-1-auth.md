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
- [ ] 1.1 Entity: `User`, `Role`, `UserRole`, `RolePermission`, `UserPermission`, `RefreshToken`
- [ ] 1.2 `AppDbContext` — калидҳо, index, snake_case naming
- [ ] 1.3 Migration `InitialAuth` + `database update`

### Permission engine
- [ ] 1.4 `Auth/Permissions.cs` — константаҳо + `All` HashSet
- [ ] 1.5 `PermissionService.ResolveAsync` — формулаи `docs/03`
- [ ] 1.6 `PermissionService.BumpVersionAsync` — версия +1 ва revoke-и refresh
- [ ] 1.7 `PermissionRequirement` + `PermissionAuthorizationHandler` (bypass барои `owner`)
- [ ] 1.8 `PermissionPolicyProvider` — policy-и динамикӣ `perm:*`
- [ ] 1.9 Extension `.RequirePermission("...")`
- [ ] 1.10 **Тест:** роль ∪ иҷозат − манъ (3 кейс)

### Токен
- [ ] 1.11 `TokenService.CreateAccessToken` — claim-ҳои `perm`, `role`, `pv`
- [ ] 1.12 `TokenService.CreateRefreshToken` — раками тасодуфӣ, дар база танҳо SHA-256
- [ ] 1.13 JWT Bearer дар `Program.cs`, `ClockSkew = TimeSpan.Zero`
- [ ] 1.14 Middleware: муқоисаи `pv`-и токен бо база → номувофиқ 401

### Seed
- [ ] 1.15 5 роли системавӣ бо permission-ҳояшон
- [ ] 1.16 Корманди Owner аз User Secrets, `must_change_password = true`
- [ ] 1.17 Seed idempotent — дубора иҷро шавад, дубликат насозад

### Endpoint-ҳои Auth
- [ ] 1.18 `POST /api/auth/login` — ҳамон матни хато барои логин ва пароли нодуруст
- [ ] 1.19 `POST /api/auth/refresh` — бо ротатсия (кӯҳна фавран revoke)
- [ ] 1.20 `POST /api/auth/logout`
- [ ] 1.21 `POST /api/auth/change-password` — мин. 8 аломат, баъд `BumpVersion`
- [ ] 1.22 `GET /api/auth/me` — роль ва permission-ҳо
- [ ] 1.23 Rate limit дар `/login` — 5 кӯшиш / дақиқа / IP

### Endpoint-ҳои Users
- [ ] 1.24 `GET /api/users` — рӯйхат бо ролҳо, филтр ва ҷустуҷӯ
- [ ] 1.25 `GET /api/users/{id}` — бо истиснопҳо
- [ ] 1.26 `POST /api/users` — логин + пароли муваққатӣ
- [ ] 1.27 `PATCH /api/users/{id}` — ном, телефон, `only_assigned`
- [ ] 1.28 `PUT /api/users/{id}/roles` — массиви `roleId`, баъд `BumpVersion`
- [ ] 1.29 `PUT /api/users/{id}/permissions` — истиснопҳо, баъд `BumpVersion`
- [ ] 1.30 `POST /api/users/{id}/reset-password`
- [ ] 1.31 `PATCH /api/users/{id}/active` — soft disable
- [ ] 1.32 Ҳимоя: Owner-и охиринро ғайрифаъол кардан мумкин нест

### Endpoint-ҳои Roles
- [ ] 1.33 `GET /api/roles`
- [ ] 1.34 `GET /api/permissions` — рӯйхати ҳама калидҳо барои UI
- [ ] 1.35 `POST /api/roles`, `PATCH /api/roles/{id}`
- [ ] 1.36 `PUT /api/roles/{id}/permissions`
- [ ] 1.37 `DELETE /api/roles/{id}` — `is_system` нест намешавад

## Definition of Done
- Юзери тестӣ бо ролҳои `developer` + `operator` сохта мешавад
- Аз Scalar login → `/auth/me` ҳарду маҷмӯи permission-ро якҷоя нишон медиҳад
- Endpoint-и `users.manage` ба ӯ **403** медиҳад
- Манъи шахсии `inbox.close` кор мекунад
- Баъди иваз кардани роль, токени кӯҳна **401** мегирад
- 5 кӯшиши нодурусти login → **429**
- Тестҳои 1.10 сабз
