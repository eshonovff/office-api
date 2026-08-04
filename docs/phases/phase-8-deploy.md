# Фазаи 8 — Deploy

**Ҳадаф:** проект дар VPS, домени воқеӣ, backup, CI/CD.
**Пешшарт:** Фазаи 4 ✅ (webhook бе домени воқеӣ санҷида намешавад)
**Тахмин:** 2 рӯз.

> ⚠️ Ин фазаро **қисман баъди Фазаи 4** иҷро кун, на дар охир.

## Вазифаҳо
- [ ] 8.1 `Dockerfile` multi-stage (sdk → runtime), non-root user
- [ ] 8.2 `docker-compose.prod.yml` — api + postgres + volume
- [ ] 8.3 DNS `office.nizom.tj` дар Cloudflare
- [ ] 8.4 Nginx reverse proxy + SSL (Let's Encrypt)
- [ ] 8.5 WebSocket proxy барои SignalR (`Upgrade`, `Connection` headers)
- [ ] 8.6 **Cloudflare proxy барои `/webhooks/*` хомӯш** — Meta бояд мустақим расад
- [ ] 8.7 Env vars дар сервер, на дар repo
- [ ] 8.8 Migration-и автоматӣ ҳангоми старт (танҳо агар `RUN_MIGRATIONS=true`)
- [ ] 8.9 Backup-и рӯзонаи PostgreSQL (`pg_dump` + cron), нигоҳдорӣ 14 рӯз
- [ ] 8.10 Санҷиши барқарорсозии backup — **як бор ҳатман иҷро кун**
- [ ] 8.11 GitHub Actions: build → test → deploy ба `main`
- [ ] 8.12 Serilog → файл, ротатсия, нигоҳдорӣ 30 рӯз
- [ ] 8.13 Uptime monitoring + огоҳии Telegram ба Owner агар `/health` афтад
- [ ] 8.14 Rate limit-и умумӣ дар Nginx

## Definition of Done
- `https://office.nizom.tj/health` аз интернет ҷавоб медиҳад
- SSL A+ дар SSL Labs
- Webhook-и Meta мерасад
- SignalR тавассути WSS кор мекунад
- Push ба `main` → deploy автоматӣ
- Backup дар cron ва як бор барқарор карда шудааст
