# Фазаи 4 — Инфраструктураи каналҳо

**Ҳадаф:** webhook-и боэътимод ва абстраксияи канал, пеш аз ягон интеграцияи воқеӣ.
**Пешшарт:** Фазаи 3 ✅
**Тахмин:** 3 рӯз.

## Дарун
Entity-ҳои канал ва чат, `IChannelProvider`, endpoint-и webhook, имзо, навбат, idempotency.

## Берун
Коди мушаххаси WhatsApp/IG/FB (фазаҳои 5 ва 7). UI-и инбокс (фазаи 6).

## Вазифаҳо
- [x] 4.1 Entity: `Channel`, `ChannelMember`, `Conversation`, `Message`, `MessageTemplate`, `WebhookLog`
- [x] 4.2 Migration + index-ҳои `docs/04`
- [x] 4.3 Шифрбандии `credentials` бо Data Protection API
- [x] 4.4 Интерфейс `IChannelProvider`: `VerifyWebhook`, `ParseWebhook`, `SendMessage`, `MarkAsRead`, `DownloadMedia`
- [x] 4.5 `ChannelProviderFactory` аз рӯи `channel.type`
- [x] 4.6 `GET /webhooks/{provider}` — hub challenge verification
- [x] 4.7 `POST /webhooks/{provider}` — санҷиши `X-Hub-Signature-256`, бе он **403**
- [x] 4.8 Webhook фавран `200` бармегардонад; коркард ба Hangfire
- [x] 4.9 Hangfire + PostgreSQL storage + dashboard дар `/hangfire` (танҳо Owner)
- [x] 4.10 Idempotency бо `messages.external_id` — UNIQUE, дубликат хомӯшона рад
- [x] 4.11 `WebhookLog` — JSON-и хом, тозакунии автоматии >30 рӯз
- [x] 4.12 Сохтани `Conversation` агар набошад (`UNIQUE(channel_id, external_id)`)
- [x] 4.13 Навсозии `last_message_at`, `unread_count`, `window_expires_at`
- [x] 4.14 CRUD-и канал: `GET/POST/PATCH/DELETE /api/channels` (`channels.manage`)
- [x] 4.15 `PUT /api/channels/{id}/members`

## Definition of Done
- Webhook-и сохта (curl бо имзои дуруст) → чат ва паём дар база
- Ҳамон webhook дубора → дубликат нест
- Имзои нодуруст → 403
- Webhook дар < 200мс ҷавоб медиҳад
