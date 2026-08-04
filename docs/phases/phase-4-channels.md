# Фазаи 4 — Инфраструктураи каналҳо

**Ҳадаф:** webhook-и боэътимод ва абстраксияи канал, пеш аз ягон интеграцияи воқеӣ.
**Пешшарт:** Фазаи 3 ✅
**Тахмин:** 3 рӯз.

## Дарун
Entity-ҳои канал ва чат, `IChannelProvider`, endpoint-и webhook, имзо, навбат, idempotency.

## Берун
Коди мушаххаси WhatsApp/IG/FB (фазаҳои 5 ва 7). UI-и инбокс (фазаи 6).

## Вазифаҳо
- [ ] 4.1 Entity: `Channel`, `ChannelMember`, `Conversation`, `Message`, `MessageTemplate`, `WebhookLog`
- [ ] 4.2 Migration + index-ҳои `docs/04`
- [ ] 4.3 Шифрбандии `credentials` бо Data Protection API
- [ ] 4.4 Интерфейс `IChannelProvider`: `VerifyWebhook`, `ParseWebhook`, `SendMessage`, `MarkAsRead`, `DownloadMedia`
- [ ] 4.5 `ChannelProviderFactory` аз рӯи `channel.type`
- [ ] 4.6 `GET /webhooks/{provider}` — hub challenge verification
- [ ] 4.7 `POST /webhooks/{provider}` — санҷиши `X-Hub-Signature-256`, бе он **403**
- [ ] 4.8 Webhook фавран `200` бармегардонад; коркард ба Hangfire
- [ ] 4.9 Hangfire + PostgreSQL storage + dashboard дар `/hangfire` (танҳо Owner)
- [ ] 4.10 Idempotency бо `messages.external_id` — UNIQUE, дубликат хомӯшона рад
- [ ] 4.11 `WebhookLog` — JSON-и хом, тозакунии автоматии >30 рӯз
- [ ] 4.12 Сохтани `Conversation` агар набошад (`UNIQUE(channel_id, external_id)`)
- [ ] 4.13 Навсозии `last_message_at`, `unread_count`, `window_expires_at`
- [ ] 4.14 CRUD-и канал: `GET/POST/PATCH/DELETE /api/channels` (`channels.manage`)
- [ ] 4.15 `PUT /api/channels/{id}/members`

## Definition of Done
- Webhook-и сохта (curl бо имзои дуруст) → чат ва паём дар база
- Ҳамон webhook дубора → дубликат нест
- Имзои нодуруст → 403
- Webhook дар < 200мс ҷавоб медиҳад
