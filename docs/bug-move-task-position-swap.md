# Bug: `PATCH /api/tasks/{id}/move` — `beforeTaskId`/`afterTaskId` иваз шудаанд

**Ёфт шуд:** 2026-08-07, ҳангоми сохтани фронтенди drag-and-drop (Фазаи 3), тавассути санҷиши воқеӣ бо `curl` ба backend-и локалӣ.
**Файл:** `Office.Api/Features/Tasks/TasksEndpoints.cs`, методи `MoveAsync` (тахминан хатти 344–398)
**Ҷиддият:** Баланд — гузоштани таск ба **сар** ё **охири** колонка (аввалин/охирин ҷой) дуруст кор намекунад. Танҳо гузоштан дар **миён** (ҳарду ҳамсоя дода шуда) ва ба **колонкаи комилан холӣ** (ҳарду `null`) дуруст аст.

## Сабаби асосӣ

`PositionCalculator.Calculate` (`Office.Api/Features/Tasks/PositionCalculator.cs`) худаш дуруст аст ва ҳуҷҷат дорад:

```csharp
/// beforePosition/afterPosition — position-ҳои ҳамсояҳои мустақими нуқтаи гузоштан.
/// null аз тарафи чап = аввали колонка; null аз тарафи рост = охири колонка.
public static double Calculate(double? beforePosition, double? afterPosition)
{
    if (beforePosition is null && afterPosition is null) return Step;
    if (beforePosition is null) return afterPosition!.Value / 2;      // ← ба сар гузоштан
    if (afterPosition is null) return beforePosition.Value + Step;    // ← ба охир гузоштан
    return (beforePosition.Value + afterPosition.Value) / 2;
}
```

Яъне дар доираи ин синф: **`beforePosition` = ҳамсояи пеш аз нуқтаи гузоштан (predecessor)**, **`afterPosition` = ҳамсояи баъд аз он (successor)**.

Аммо `MoveAsync` (хатти 355–369) онҳоро аз рӯи **номи майдони дархост** мегирад, на аз рӯи маънои воқеии он:

```csharp
if (request.BeforeTaskId is not null)
{
    var before = siblings.FirstOrDefault(t => t.Id == request.BeforeTaskId);
    beforePosition = before.Position;   // ← БОРБА: BeforeTaskId маънояш "таск баъд аз ин мемонад"
}                                        //   (яъне ин таск SUCCESSOR-и таски кӯчонидашуда аст),
                                         //   на predecessor!
if (request.AfterTaskId is not null)
{
    var after = siblings.FirstOrDefault(t => t.Id == request.AfterTaskId);
    afterPosition = after.Position;     // ← ҳамин тавр, AfterTaskId воқеан PREDECESSOR аст
}

var newPosition = PositionCalculator.Calculate(beforePosition, afterPosition);
```

`MoveTaskRequest.BeforeTaskId` аз рӯи маънои табиии API (ва тавре фронтенд онро истифода мебарад): **"таски кӯчонидашуда бояд ПЕШ АЗ ин ID биистад"** — яъне таски кӯчонидашуда **predecessor**-и `BeforeTaskId` мешавад, пас худи `BeforeTaskId` дар аслаш **successor** (= `afterPosition`-и `PositionCalculator`) аст. Ҳамин тавр баръакс барои `AfterTaskId` (= `beforePosition`).

Дар натиҷа, дар ҳар ду ҷои код (ҳисоби `newPosition` ва ҳисоби `insertIndex` дар блоки reindex, хатти 376–378) майдонҳо **иваз шудаанд**:

```csharp
var insertIndex = request.BeforeTaskId is null
    ? 0                                                              // ← бояд аз рӯи AfterTaskId бошад
    : ordered.FindIndex(t => t.Id == request.BeforeTaskId) + 1;      // ← бояд AfterTaskId бошад
```

## Чаро санҷиши "миён" гузашт, вале "аввал/охир" не

Формулаи миёна `(before + after) / 2` **симметрӣ** аст — новобаста аз он ки кадом ID ба кадом параметр меравад, натиҷа якхела мебарояд. Бинобар ин ҳолати "ҳарду ҳамсоя дода шуда" ҳамеша дуруст ба назар мерасид, ҳатто бо иваз. Аммо формулаҳои канорӣ (`afterPosition/2` ва `beforePosition+Step`) симметрӣ **нестанд** — маҳз онҳо хатогиро ошкор карданд.

## Такрористеҳсоли санҷидашуда (curl, 2026-08-07)

Колонка бо A(position=1000), B(position=2000):

| Дархост | Интизор (аз рӯи маънои табиии API) | Воқеан гирифта шуд | Сабаб |
|---|---|---|---|
| `beforeTaskId=A, afterTaskId=null` (B ба сар гузоштан, пеш аз A) | B → 500 | Ҳеҷ тағйирот (B ҳамон 2000 монд) | `beforePosition=A(1000)`, `afterPosition=null` → шохаи "охир" → `1000+1000=2000` — ин айнан position-и кунунии B буд, бинобар ин "тағйирнашуда" намуд |
| `beforeTaskId=null, afterTaskId=B` (A ба охир гузоштан, баъд аз B) | A → 3000 | Ҳеҷ тағйирот (A ҳамон 1000 монд) | `beforePosition=null`, `afterPosition=B(2000)` → шохаи "аввал" → `2000/2=1000` — айнан position-и кунунии A |
| Кӯчонидани таски нав C ба колонкаи дигар бо `beforeTaskId=null, afterTaskId=A` (C баъд аз A) | C → пас аз A | C → **пеш аз** A (position=500) | Ҳамин иваз — C ҳамчун "аввал" ҳисоб шуд, на "баъд аз A" |

## Тавсияи ҳал

Дар `MoveAsync`, ду майдони local variable-ро мутобиқи маънои воқеии онҳо иваз кунед (на номи майдони дархост):

```csharp
double? predecessorPosition = null; // = "afterTaskId"-и дархост (таски кӯчонидашуда БАЪД аз ин мемонад)
double? successorPosition = null;   // = "beforeTaskId"-и дархост (таски кӯчонидашуда ПЕШ АЗ ин мемонад)

if (request.AfterTaskId is not null)
{
    var predecessor = siblings.FirstOrDefault(t => t.Id == request.AfterTaskId);
    if (predecessor is null) return Results.BadRequest();
    predecessorPosition = predecessor.Position;
}

if (request.BeforeTaskId is not null)
{
    var successor = siblings.FirstOrDefault(t => t.Id == request.BeforeTaskId);
    if (successor is null) return Results.BadRequest();
    successorPosition = successor.Position;
}

var newPosition = PositionCalculator.Calculate(predecessorPosition, successorPosition);

// ...ва дар блоки reindex:
var insertIndex = request.AfterTaskId is null
    ? 0
    : ordered.FindIndex(t => t.Id == request.AfterTaskId) + 1;
```

Баъд аз ислоҳ, тавсия мешавад тести 2.21-и `phase-2-tasks.md`-ро бо ҳолатҳои "яктои `null`" (на танҳо "миён" ва "ҳарду `null`") такмил диҳед — маҳз ҳамин ду ҳолат хатогиро пинҳон карда буданд.

## Робита бо frontend

Фронтенди Фазаи 3 (`office-web`, `app/lib/position.ts` — `getMoveNeighbors`) семантикаи дурустро (мутобиқи маънои табиии API, на коди феълии backend) татбиқ кардааст ва то ин ҷо тағйир дода **нашудааст** — вақте ин баг ҳал шавад, frontend бидуни тағйирот бояд дуруст кор кунад.
