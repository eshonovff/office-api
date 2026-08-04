# Фазаи 7 — Instagram ва Facebook

**Ҳадаф:** ду канали боқимонда дар ҳамон инбокс.
**Пешшарт:** Фазаи 6 ✅
**Тахмин:** 3 рӯз.

## Пеш аз код
- [ ] Instagram ба Facebook Page пайваст (роҳи Facebook Login for Business — як app, ҳарду канал)
- [ ] Аккаунти Instagram навъи Business ё Creator
- [ ] Permission-ҳо дар Meta App илова шудаанд

> **App Review лозим нест** — ин аккаунтҳои худамонанд, Standard Access кифоя аст.
> App Review танҳо вақте лозим мешавад, ки ин платформа ба мижозон фурӯхта шавад.

## Вазифаҳо
- [ ] 7.1 OAuth flow — Facebook Login for Business, гирифтани Page access token
- [ ] 7.2 Нигоҳдории токен (шифрбандӣ) + long-lived exchange
- [ ] 7.3 Hangfire job — refresh-и токен пеш аз тамом шудан
- [ ] 7.4 `MessengerProvider` — матн, расм, файл, quick reply
- [ ] 7.5 `InstagramProvider` — мерос аз базаи умумӣ бо `MessengerProvider` (~80% умумӣ)
- [ ] 7.6 Instagram: story reply, story mention, post share — навъҳои алоҳидаи паём
- [ ] 7.7 Тирезаи 24-соата барои ҳарду
- [ ] 7.8 Тэги `human_agent` — дароз кардани тиреза то 7 рӯз
- [ ] 7.9 **Маҳдудияти муҳим:** аввал фиристодан мумкин нест — мижоз бояд аввал нависад. Endpoint инро санҷад ва хатогии фаҳмо диҳад
- [ ] 7.10 Як endpoint-и webhook барои ҳарду (Meta ҳарду event-ро ба як URL мефиристад) — роутинг аз рӯи `object` дар body

## Definition of Done
- DM аз Instagram → дар инбокс
- Паём аз Facebook Page → дар инбокс
- Ҷавоб ба ҳарду мерасад
- Story reply дуруст парс мешавад
- Ҳамаи се канал дар як рӯйхат бо иконкаи худашон
