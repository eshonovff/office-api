# Фазаи 13 — Обунаи мизоз (trial + пардохти дастӣ бо тасдиқи модератор)

**Ҳадаф:** мизози худсабтшуда (`Customer`, ниг. `feat(customer-auth)`) 7 рӯз ройгон
кор мекунад, баъд бояд тариф харад. Пардохт — бе гейтвейи онлайн: мизоз ба корти
ширкат (Душанбе Сити / Алиф) маблағи **тасодуфӣ** (мас. 200.37) мегузаронад, скриншоти
чекро бор мекунад, модератор (корманд) дида тасдиқ/рад мекунад.
**Пешшарт:** customer-auth (email+parol, Google) ✅

> Қисми дуюми нақша (multi-tenancy — ҳар мизоз каналҳо ва автоматизатсияи худашро
> дорад) — фазаи алоҳида, **баъд аз ин**. Ин фаза ба Channel/Flow/Automation даст
> намезанад.

## Тасмимҳои тарроҳӣ

1. **`PlanTier` дар `Customer`** (`Pro | Creator | Premium`, nullable — `null` = тарифи
   пулакӣ нест) + `PlanExpiresAt` + `TrialEndsAt`. Ҳолати воқеӣ (trial / фаъол / гузашта)
   ҳеҷ гоҳ дар DB захира намешавад — ҳар дафъа аз ин се майдон ва `now` ҳисоб мешавад
   (`CustomerAccessResolver`, pure), то "гузашт"-и мӯҳлат job-и алоҳида талаб накунад.
2. **Нархҳо ва кортҳо — дар конфигуратсия** (`Subscriptions:*`), на дар DB. Саҳифаи
   админ (баъдтар, дар системаи кормандон) онҳоро ба DB мекӯчонад — ҳоло YAGNI.
3. **Маблағи тасодуфӣ** = нархи моҳона × моҳҳо + 0.01–0.99 сомонӣ (дирам), дар байни
   дархостҳои кушода (AwaitingPayment/Pending) то имкон такрорнашаванда
   (`PaymentAmountGenerator`, pure). Гузаронидани корт-ба-корт ҳеҷ comment надорад —
   ин дирамҳо ягона роҳи ёфтани "кӣ гузаронд" дар выпискаи бонк аст.
4. **Ҳолатҳои дархост:** `AwaitingPayment` (маблағ дода шуд, чек ҳанӯз не) → `Pending`
   (чек бор шуд, интизори модератор) → `Approved | Rejected`. `Cancelled` — дархости
   AwaitingPayment, ки бо дархости нави ҳамон мизоз иваз шуд.
5. **Як дархости кушода барои як мизоз:** дархости нав AwaitingPayment-и қаблиро
   Cancelled мекунад; агар Pending бошад — 409 (то ду маротиба пардохт накунад).
6. **Тамдид:** `PlanExpiresAt = max(now, PlanExpiresAt-и ҷорӣ, TrialEndsAt) + моҳҳо`,
   тариф = тарифи дархост (`SubscriptionPeriodCalculator`, pure). Пардохти пеш аз мӯҳлат
   рӯзҳои боқимондаро намесӯзонад — на рӯзҳои тарифи ҷорӣ, на рӯзҳои trial.
7. **Чек** — jpg/jpeg/png/webp/pdf, то 10 МБ, `uploads/subscription-receipts/{customerId}/`.
   Ҳеҷ гоҳ ҷамъиятӣ нест — танҳо endpoint-и модератор бо `subscriptions.manage`.
7а. **Корти пардохт** — ҳангоми бор кардани чек мизоз корте, ки ба он гузаронд, интихоб
   мекунад (`cardNumber`, ҳатмӣ, бояд дар `Subscriptions:PaymentCards` бошад). Бонк ва рақам
   snapshot мешаванд (`PaidToBank`/`PaidToCardNumber`), то модератор донад таърихи кадом
   бонкро бинад — ҳатто агар корт баъдтар аз конфигуратсия бардошта шавад.
7б. **Мӯҳлати пардохт** — `Subscriptions:PaymentWindowMinutes` (5): AwaitingPayment баъд аз
   ин мӯҳлат `Expired` мешавад ва маблағаш озод. Job нест — ҳар хондан/дархости нав аввал
   `SubscriptionRequestExpiry` мегузаронад. Upload боз 1 дақ grace дорад (чеке, ки дар 4:59
   интихоб шуд, метавонад баъди 5:00 расад). Frontend таймер нишон медиҳад ва дар 0 ба
   `/account` мебарад.
8. **Permission-и нав `subscriptions.manage`** — Owner/Admin (seeder онро ба ролҳои
   мавҷуда худкор илова мекунад).
9. **Trial** — `TrialEndsAt = лаҳзаи тасдиқи email + Subscriptions:TrialDays` (пешфарз 7),
   на `CreatedAt`: ҳисоби тасдиқнашуда наметавонад ворид шавад, пас рӯзҳояш набояд сӯзанд.
   Email — дар `verify-email`; Google/Apple — дар ворид/пайвастшавии аввал (email-и онҳо
   аллакай тасдиқшуда). `??=` — такрор trial-ро нав намекунад. Migration барои мизозони
   аллакай тасдиқшуда аз `email_verified_at` пур мекунад.

## Вазифаҳо

### Backend
- [x] 13.1 `Customer`: `PlanTier`, `PlanExpiresAt`, `TrialEndsAt` + enum `CustomerPlanTier`
- [x] 13.2 `SubscriptionRequest` entity + конфигуратсия + migration (бо backfill-и `trial_ends_at`)
- [x] 13.3 `CustomerAccessResolver` (pure) + тест
- [x] 13.4 `PaymentAmountGenerator` (pure) + тест
- [x] 13.5 `SubscriptionPeriodCalculator` (pure) + тест
- [x] 13.6 `SubscriptionCatalog` — нархҳо/кортҳо/trial-days аз `Subscriptions:*`
- [x] 13.7 `TrialEndsAt` ҳангоми тасдиқи email ва Google/Apple
- [x] 13.8 `GET /api/public/auth/me` → объекти `access` (ҳолат, тариф, то кай)
- [x] 13.9 Мизоз: `GET /api/public/subscriptions/catalog`, `POST .../requests`,
      `POST .../requests/{id}/receipt`, `GET .../requests`
- [x] 13.10 Permission `subscriptions.manage`
- [x] 13.11 Модератор: `GET /api/subscription-requests`, `GET .../{id}/receipt`,
      `POST .../{id}/approve`, `POST .../{id}/reject`

### Frontend (office-web)
- [x] 13.12 Саҳифаи тарифҳо + ҷараёни пардохт (маблағ, корт, бор кардани чек) — `/account/billing`
- [x] 13.13 Ҳолати обуна дар sidebar-и мизоз (trial: N рӯз / тариф то сана / гузашт)
- [ ] 13.14 Саҳифаи модератор дар системаи кормандон — **баъдтар**, қарори корбар

## Definition of Done

1. `dotnet build` бе warning, `dotnet test` сабз
2. Ҷараёни пурра зинда санҷида шуд: сабти ном → trial дар `/me` → дархост → маблағи
   тасодуфӣ → бор кардани чек → тасдиқи модератор (curl бо токени Owner) → `/me` тарифи
   фаъолро нишон медиҳад
3. Мизоз чеки мизози дигарро дида/иваз карда наметавонад; бе `subscriptions.manage`
   endpoint-ҳои модератор 403

### Натиҷаи санҷиши зинда (backend, 2026-09-24)

- Backfill: ду мизози аллакай тасдиқшуда `trial_ends_at = email_verified_at + 7 рӯз` гирифтанд.
- verify-email → `access = Trial`, `endsAt` = тасдиқ + 7 рӯз.
- Каталог; тарифи нодуруст / тарифи берун аз каталог / муддати 2 ё 0 моҳ → 400.
- Дархости дуюм дархости AwaitingPayment-и аввалро `Cancelled` кард; маблағҳо 200.19, 200.40.
- Чек: `.txt` → 400; id-и бегона/номаълум → 404; иваз кардани чек файли кӯҳнаро нест мекунад.
- Дархости нав ҳангоми Pending → 409.
- Owner: навбат, чек (`image/png`, байтҳо якхела), тасдиқ → `/me` = `Active Pro`,
  `endsAt` = охири trial + 1 моҳ. Тасдиқ/рад такрорӣ → 409, id-и номаълум → 404,
  рад бе сабаб → 400, чек баъд аз тасдиқ → 409.
- Рад бо сабаб → мизоз сабабро дар рӯйхаташ мебинад, `access` тағйир намеёбад.
- Се тасдиқи ҳамзамон: як 200, ду 409; мӯҳлат як бор тамдид шуд (+3 моҳ).
- Токени мизоз дар endpoint-и модератор → 401, токени корманд дар endpoint-и мизоз → 401.
- Хатои Resend (email-и тестӣ) танҳо warning аст — тасдиқ/радро вайрон намекунад.
- 403 барои корманди бе `subscriptions.manage` — аз ҳамон `RequirePermission`-и умумӣ;
  зинда бо корманди алоҳида санҷида нашуд.

### Frontend (office-web `feat/fe-phase-13-subscriptions`, 2026-09-24)

Дар Chrome-и headless бо корти тестии муваққатӣ (env var, на config) дида шуд, desktop ва 390px:
trial дар sidebar → интихоби тариф → маблағи ягона + корт → чек → "дар санҷиш" → тасдиқи Owner →
`Тарифи Pro то 01.11.2026` → рад бо сабаб дар таърих → мӯҳлат гузашт (сурх) → иваз кардани тариф.
Бе `Subscriptions:PaymentCards` тугмаи харид хомӯш аст ва паём нишон дода мешавад.
