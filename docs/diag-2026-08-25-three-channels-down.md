# Ташхис: ҳар се канал (WhatsApp, Instagram, Facebook) паём намерасонданд

**Санҷида шуд:** 2026-08-25, тавассути `curl`/`psql`/`cloudflared` бевосита, бе тахмин.
**Сабаби умумӣ (ҳар се канал):** `cloudflared`-и tunnel (`office-dev`, домейн `dev-office.nizom.tj`) кор намекард — на процесс, на пайвастшавӣ ба edge-и Cloudflare буд. Backend худаш комилан солим буд.

## Ҷадвали натиҷа

| Канал | Қабати шикаста | Далел | Сабаби аслӣ | Ислоҳи пешниҳодшуда |
|---|---|---|---|---|
| WhatsApp | 1 (Tunnel) | `webhook_logs`: сатри охирин 2026-08-20 (5 рӯз холӣ) — дар ҳоле ки backend бо тести дастӣ 200 медиҳад | Tunnel умуман кор намекард | Tunnel-ро ислоҳ кардам (поён). **Далели воқеӣ ҳанӯз нест** — то ҳол ягон webhook-и воқеии WhatsApp пас аз ислоҳ наомадааст (эҳтимол танҳо аз сабаби набудани паёми воқеӣ, на боз ҳам баг) |
| Instagram | 1 (Tunnel) | `webhook_logs`: сатри охирин то ислоҳ — 07:26:33, пас аз ислоҳ — 22 webhook дар 32 сония, 0 хато | Tunnel умуман кор намекард | Ислоҳ шуд, **бо далели зинда тасдиқ шуд** (poён) |
| Facebook | 1 (Tunnel) — асосӣ; + як мушкили дуюми ҷудогона | Ҳамон 6с19д холигии `webhook_logs`; илова: `subscribed_apps` ҳозир танҳо `messages, messaging_postbacks` дорад — `message_echoes` бе сабаби возеҳ дубора нопадид шуд | Tunnel умуман кор намекард | Tunnel ислоҳ шуд, **бо далели зинда тасдиқ шуд** (poён). Мушкили дуюм (message_echoes) ба таъмири алоҳида ниёз дорад — ниг. поён |

## Қабат 1: Tunnel/раванд — сабаби аслӣ

Далелҳо (пеш аз ислоҳ):
```
$ ps aux | grep cloudflared        → холӣ (ягон процесс намеравад)
$ cloudflared tunnel list          → office-dev, сутуни CONNECTIONS холӣ
$ curl https://dev-office.nizom.tj/health → HTTP 530 (хатои худи Cloudflare: "origin/tunnel нест")
$ brew services list | grep cloudflare → cloudflared  none
```
`brew services`-и Homebrew барои `cloudflared` plist-и умумӣ дорад (бе `--url`/`--config`) — яъне tunnel ҳеҷ гоҳ ҳамчун сервис оғоз нашудааст, балки дастӣ дар терминал (ки баъдан пӯшида/бас шудааст — эҳтимол ба монанди чизе ки бо backend низ рӯй дод, ниг. `dev.sh`).

**Ислоҳ:**
```
cloudflared tunnel --url http://localhost:5056 run office-dev
```
Баъд аз ин: `curl https://dev-office.nizom.tj/health` → **200**.

⚠️ **Хатари такрор:** ин tunnel ҳоло ҳам бе назорати воқеӣ кор мекунад (на launchd/systemd, на `brew services`-и дуруст танзимшуда бо `--url`). Агар терминал баста шавад ё компютер аз нав сар шавад, боз ҳам хомӯшона мемирад — бе ягон огоҳӣ. Тавсия: launchd plist-и дуруст (бо `--url http://localhost:5056 run office-dev` ва `KeepAlive`) созед, то ин ҳодиса такрор нашавад.

## Қабат 2–7: барои ҳар се канал алоҳида санҷида шуд — ҳама СОЛИМ

Барои ҷудо кардани "танҳо tunnel" аз "чизи дигар низ шикастааст", ба `localhost:5056` (бидуни tunnel) се дархости имзошудаи воқеӣ фиристода шуд — бо ҳамон алгоритми HMAC-SHA256-и худи `WebhookSignature.cs` ва secret-ҳои воқеии `user-secrets`:

```
POST localhost:5056/webhooks/whatsapp   → 200, webhook_logs.error = NULL, Message сохта шуд (Inbound, WhatsApp)
POST localhost:5056/webhooks/facebook   → 200, webhook_logs.error = NULL, Message сохта шуд (Inbound, Facebook)
POST localhost:5056/webhooks/instagram  → 200, webhook_logs.error = NULL, Message сохта шуд (Inbound, Instagram)
```
(Се паёми синтетикӣ баъд аз санҷиш аз DB нест карда шуданд — ифлоскунии inbox-и воқеӣ набояд монад.)

Ин собит мекунад:
- **Қабати 3 (Имзо):** ҳар се calidi secret дар `user-secrets` мавҷуданд ва дуруст: `Webhooks:AppSecret` (WhatsApp+Facebook, як app), `Meta:Instagram:AppSecret` (app-и алоҳида), `Webhooks:VerifyToken`.
- **Қабати 4 (Route/парсер):** ҳар се endpoint дуруст кор мекунанд, парсер паёми воқеӣ мебарорад (сифр не).
- **Қабати 5 (channels):** ҳар се сатри канал бо `external_id`-и дуруст мувофиқанд:
  - WhatsApp: `1206432455895142` = phone_number_id (мувофиқ).
  - Instagram: `17841438754823969` = IG Business Account id (мувофиқ).
  - Facebook: `1221452587711299` (пештар дар ин чат тасдиқ шуда буд — id-и Page-и обунашуда ба app 3289534717918806).
  - Ҳар се: `is_active=true`, `requires_reconnect=false`, `webhook_setup_warning=NULL`.
  - Ёддошт: `Instagram.credentials_expires_at` дар DB `NULL` аст — ғайримунтазир (токени дарозмуддат бояд мӯҳлат дошта бошад). Худи токен ҳоло эътибор дорад (поён), вале ин арзиши холӣ метавонад маънои онро дошта бошад, ки `InstagramTokenRefreshJob` ҳанӯз як бор ҳам иҷро нашудааст ё вақти пайвастшавӣ ин майдон сабт нашудааст — арзиши тафтиши алоҳида, на фаврӣ.
- **Қабати 6 (Токенҳо):**
  - WhatsApp: `GET /{phone_number_id}?fields=status,display_phone_number` → `{"status":"CONNECTED",...}`.
  - Instagram: `GET graph.instagram.com/me?fields=id,username` → `{"id":"28625914253673240","username":"eshonov.f1"}`.
  - Facebook: аввалин санҷиш (`/{page-id}?fields=id,name`) 400 дод — вале ин хатои **иҷозат** буд (`pages_read_engagement` набуд, ки мо қасдан дархост накардаем), на токени мурда. Бо дархости дуруст (`/{page-id}/subscribed_apps?fields=subscribed_fields`, ки ба scope-и мо мувофиқ аст) → 200, токен эътибор дорад.
- **Қабати 7 (webhook_logs):** ҳар се провайдер: сатри охирин пеш аз ислоҳ — **2026-08-25 07:26:33**, ҳозир (вақти санҷиш) — **13:45:59** → 6 соату 19 дақиқа сатри НАВ нест барои ҲАР СЕ якбора. Пас аз ислоҳи tunnel, дар зарфи 30 сония 22 webhook-и ВОҚЕИИ Instagram (на синтетикӣ) омаданд — Meta онҳоро дар давоми хомӯшӣ нигоҳ дошта буд ва баъд аз барқарорсозӣ якбора фиристод. Ҳама 0 хато.

## Қабат 8 (Realtime/фронт)

То ислоҳи Қабати 1 санҷида НАШУДААСТ — комилан аз он вобаста буд (агар webhook намерасад, чизе барои SignalR фиристодан ҳам нест). Ҳоло ки Instagram воқеан паём мегирад, тавсия — дар UI/inbox худи шумо тафтиш кунед, ки паёмҳои воридотии Instagram зинда намоён мешаванд ё не. Агар дар DB бошанд, вале дар UI не — ин масъалаи алоҳидаи Realtime аст, на ин ташхис.

## Мушкили дуюми Facebook (ҷудо аз tunnel)

`GET /1221452587711299/subscribed_apps?fields=subscribed_fields` ҳозир:
```json
{"data":[{"id":"3289534717918806","subscribed_fields":["messages","messaging_postbacks"]}]}
```
`message_echoes` нест — дар ҳоле ки дар ҳамин чат пештар (2026-08-25, ниг. ёддошти қаблӣ) бо POST ба се майдон обуна карда шуда буд ва бо GET тасдиқ шуда буд. Сатҳи App Dashboard ҳоло дуруст аст (се майдон фаъол — шумо худатон ислоҳ карда будед). Пас чизе байни он лаҳза ва ҳозир сатҳи Page-ро аз 3 ба 2 майдон баргардондааст — сабаби дақиқаш номаълум (эҳтимол таъсири канории таҳрири Dashboard, эҳтимол чизи дигар). Ин ба паёми **оддии** Facebook таъсир НАМЕРАСОНАД (`messages` ҳоло ҳам фаъол аст), танҳо ба echo-и паёмҳое, ки шумо мустақим аз барномаи Facebook мефиристед.

**Ислоҳи пешниҳодшуда:** `POST /1221452587711299/subscribed_apps?subscribed_fields=messages,messaging_postbacks,message_echoes` дубора. Ин ҳамон API аст, ки `FacebookOAuthConnector.EnsureWebhookSubscriptionAsync` (аллакай дар код) иҷро мекунад — метавонам онро тавассути тугмаи "Пайваст аз нав" дар /channels иҷро кунам (бе тағйири нав дар код), ё худи шуморо мехоҳам иҷозат диҳед, то ман бевосита занги API занам.

## Чизҳое, ки аз шумо лозим аст

1. **WhatsApp-ро воқеан санҷед**: аз телефони худ ба рақами тестӣ (`+1 555-654-8185`, phone_number_id `1206432455895142`) як паём фиристед. То ҳол факти зиндаи webhook-и WhatsApp тасдиқ нашудааст (танҳо синтетикӣ) — 5 рӯз аст сатри нав нест, ва мехоҳам мутмаин шавам, ки ин танҳо аз набудани паёми воқеӣ аст, на боз ҳам баг.
2. **Иҷозати ислоҳи Facebook message_echoes**: тавассути занги API (бе тугма) ё тавассути тугмаи "Пайваст аз нав" дар /channels — кадомашро мехоҳед?
3. **Қарор дар бораи назорати tunnel**: мехоҳед launchd plist-и дуруст созам (то дигар хомӯшона намирад), ё ин корро худатон мекунед?
