# Runbook — деплойи office-api дар сервери зинда (дар паҳлӯи NIZOM CRM)

> Ин деплой пеш аз мӯҳлати расмии фазаи 8 карда шуд — танҳо барои санҷиши
> webhook-и WhatsApp (фазаи 5) HTTPS-и воқеӣ лозим буд. Ҳама чиз изолятсия
> шудааст, ба NIZOM CRM даст намерасонад. (Санаи 2026-08-08, PROGRESS.md.)

## 0. Пешшарт (як бор санҷед)

- DNS: `office.nizom.tj` бояд ба IP-и ҳамин сервер ишора кунад (`A` record).
  Санҷиш: `dig +short office.nizom.tj` — бояд IP-и серверро баргардонад.
- Портҳои `5100` ва `5435` дар сервер холӣ бошанд:
  `sudo ss -tlnp | grep -E ':5100|:5435'` — бояд чизе набарорад.
- Docker аллакай насб аст (барои NIZOM CRM). Санҷиш: `docker --version`.

## 1. Насби асбобҳои иловагӣ (агар набошанд)

```bash
sudo apt-get update
sudo apt-get install -y nginx certbot python3-certbot-nginx git
```

## 2. Гирифтани код

```bash
sudo mkdir -p /opt/office
sudo chown "$USER":"$USER" /opt/office
cd /opt/office
git clone git@github.com:eshonovff/office-api.git
cd office-api
git checkout dev   # ё main — санҷед кадоме тайёр аст
```

## 3. Танзими секретҳо

```bash
cp deploy/env.production.example .env
nano .env
```
Ҳамаи `<ЗАМИНАИ_ХОЛӢ>`-ро пур кунед:
- `POSTGRES_PASSWORD` — пароли нави дилхоҳ (тасодуфӣ)
- `JWT_KEY` — тавассути `openssl rand -base64 48` месозед
- `SEED_OWNER_PASSWORD` — пароли Owner-и аввалини ин муҳити прод (аз пароли dev **фарқ** кунад)
- `SMS_*` — маълумоти воқеии OsonSMS
- `WEBHOOKS_APP_SECRET` — App Secret-и воқеии Meta App (Settings → Basic)
- `WEBHOOKS_VERIFY_TOKEN` — матни дилхоҳи худатон (мас. `office-nizom-verify-2026`)

## 4. Сар додани контейнерҳо

```bash
./deploy.sh
```
Ин `git pull` мекунад (аллакай нав аст — беэътибор), image месозад, контейнерҳоро сар медиҳад ва интизори `/health` мешавад.

Санҷиши дастӣ:
```bash
docker compose -f docker-compose.prod.yml ps
docker compose -f docker-compose.prod.yml logs api --tail=50
curl -s http://127.0.0.1:5100/health
```
Бояд `Healthy` баргардад ва дар лог "Migrations applied successfully" бошад.

## 5. Nginx

```bash
sudo cp deploy/nginx/office.nizom.tj.conf /etc/nginx/sites-available/office.nizom.tj
sudo ln -s /etc/nginx/sites-available/office.nizom.tj /etc/nginx/sites-enabled/office.nizom.tj
sudo nginx -t
sudo systemctl reload nginx
```
`nginx -t` бояд "syntax is ok" ва "test is successful" гӯяд. Агар хатогӣ дар бораи файли дигар (мас. `nizom.tj`) бошад — ин файлро таҳрир **накардаед**, хатогӣ аз ҷои дигар аст, истаед.

Санҷиш: `curl -s http://office.nizom.tj/health` (ҳанӯз бе HTTPS) → `Healthy`.

## 6. SSL (certbot)

```bash
sudo certbot --nginx -d office.nizom.tj
```
Саволҳои certbot: email гузоред, шартнома қабул кунед, redirect HTTP→HTTPS-ро **ҳа** гӯед. Ин танҳо файли `office.nizom.tj`-ро таҳрир мекунад — файли дигар дахл намекунад.

Санҷиш: `curl -s https://office.nizom.tj/health` → `Healthy`.

## 7. Санҷиши пурра (Definition of Done)

- [ ] `curl -s https://office.nizom.tj/health` → `Healthy`
- [ ] `curl -s -X POST https://office.nizom.tj/api/auth/login -H "Content-Type: application/json" -d '{"username":"owner","password":"<SEED_OWNER_PASSWORD>"}'` → `200`, accessToken
- [ ] Дар Meta App dashboard → WhatsApp → Configuration → Webhook: URL = `https://office.nizom.tj/webhooks/whatsapp`, Verify Token = ҳамон `WEBHOOKS_VERIFY_TOKEN` → тугмаи "Verify and save" → бе хатогӣ
- [ ] `POST /api/channels` бо credentials-и воқеии WhatsApp (тавассути owner token)
- [ ] Аз телефони шахсӣ ба рақами тестӣ SMS → `docker compose -f docker-compose.prod.yml logs api` дар он паём намоён

## Қадамҳои оянда

Вақте `office-web` (frontend) тайёр шавад, `deploy/nginx/office.nizom.tj.conf`
тағйир меёбад:
- `location /` — ба хидмати файлҳои статикӣ (`root /opt/office/web; try_files $uri /index.html;`)
- `location /api/` ва `location /webhooks/` — блокҳои алоҳида ба backend (порти 5100) илова мешаванд

Ҳозир ин лозим нест — `location /` бевосита ба backend proxy мекунад,
чунки frontend ҳанӯз дар ин сервер ҷойгир нашудааст.

## Маҳдудияти маълум

DataProtection keys (`/var/office/keys`, барои шифри `channel.credentials_encrypted`)
дар диск бе шифри иловагӣ (XML-и оддӣ) захира мешаванд — ин ҳамон рафтори
пешфарзи .NET дар Linux аст (сертификати X509 танзим нашудааст). Барои
санҷиши webhook кофист; агар ин деплой доимӣ шавад (фазаи 8), бояд
`ProtectKeysWithCertificate` ё монанди он илова шавад.

## Бозгашт (агар чизе шикаст)

Ҳама чиз изолятсия шудааст — ба NIZOM CRM ҳеҷ таъсире надорад, барои ҳамин бозгашт содда аст:

```bash
# Танҳо истодан (маълумот мемонад, метавонед бори дигар сар диҳед)
docker compose -f docker-compose.prod.yml down

# Nginx-ро бекор кардан (файли NIZOM CRM дахл намеёбад)
sudo rm /etc/nginx/sites-enabled/office.nizom.tj
sudo nginx -t && sudo systemctl reload nginx

# Пурра нест кардан (маълумот, volume-ҳо ҳам меравад)
docker compose -f docker-compose.prod.yml down -v
sudo rm -rf /opt/office
sudo rm -f /etc/nginx/sites-available/office.nizom.tj
sudo certbot delete --cert-name office.nizom.tj   # агар SSL сохта шуда бошад
```

Санҷиши баъд аз бозгашт: `curl -s https://nizom.tj` (сайти асосӣ) бояд ҳамон тавре кор кунад, ки пеш аз ин деплой кор мекард — чизе тағйир наёфтааст.
