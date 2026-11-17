# Архитектура «Свідок»

Документ описывает, как устроен код: слои, главный цикл, поток данных от события до действия
NPC и точки расширения. Все имена классов соответствуют исходникам.

## Слои и зависимости

```
Svidok.Game   → Svidok.Core, Svidok.Llm
Svidok.Runner → Svidok.Core, Svidok.Llm
Svidok.Llm    → Svidok.Core (только модели данных)
Svidok.Core   → ничего (ни Raylib, ни HTTP, ни токенизатора)
```

`Svidok.Core` не знает про графику и сеть: это позволяет гонять ту же логику в окне и в консоли
и держать детерминизм.

## Проекты и папки

| Папка | Назначение | Ключевые типы |
|---|---|---|
| `Core/World` | карта, зоны, время, случайность, поиск пути | `GameMap`, `Zone`, `GameClock`, `Rng`, `Pathfinding`, `World` |
| `Core/Entities` | персонажи, предметы, расписания | `Npc`, `Player`, `ItemDef`, `ScheduleEntry`, `Role`, `Activity` |
| `Core/Events` | журнал и фоновые события | `WorldEvent`, `EventLog`, `EventSchema`, `FillerGenerator` |
| `Core/Perception` | зрение и слух | `LineOfSight`, `PerceptionSystem` |
| `Core/Knowledge` | знания, слухи, доверие, искажения | `KnowledgeEntry`, `KnowledgeBase`, `Credibility`, `TrustMatrix`, `ConversationSystem`, `DistortionEngine`, `ClaimExtractor` |
| `Core/Decisions` | варианты V0–V4, действия, валидатор | `Variant`, `ActionCatalog`, `Validator`, `ActionExecutor`, `IActionHandler`, `RuleDecisionMaker`, `DecisionTaskQueue` |
| `Core/Quests` | задания | `Quest`, `QuestSystem` |
| `Core/Scenarios` | сценарии и главный цикл | `ScenarioDefinition`, `ScenarioLoader`, `Simulation`, `MovementSystem`, `ScenarioRunner`, `BatchOutcome` |
| `Core/Metrics` | формулы и CSV | `MetricsCollector`, `CsvWriter`, `CostCalculator`, `LeakDetector` |
| `Llm` | клиент модели и промпты | `DeepSeekClient`, `CachedLlmClient`, `MockLlmClient`, `PromptBuilder`, `ResponseParser`, `LlmDecisionMaker`, `TokenCounter`, `LlmBootstrap` |
| `Game` | окно Raylib + ImGui | `GameApp`, `SimulationHost`, `WorldRenderer`, `ObserverPanel`, `DialogWindow`, `PlayerController`, `MenuScreen` |
| `Runner` | пакетный запуск | `Program`, `CliOptions` |

## Главный цикл

Один тик — одна игровая минута. `Simulation.TickAsync` выполняет шаги строго по ТЗ (раздел 4.2):

1. **Готовые решения модели** — `DecisionTaskQueue.TryDequeue` → `ApplyDecision`
   (валидатор для V0/V4, затем `ActionExecutor`).
2. **События сценария** — `RunScript` (обычные события, `spawn`, `set_param`).
3. **Фоновые события** — `FillerGenerator.GenerateHour` в начале каждого игрового часа.
4. **Движение** — `MovementSystem.Update`: расписание, патруль, активные действия.
5. **Фильтр восприятия** — `PerceptionSystem.Process` по событиям с прошлого тика.
6. **Разговоры** — `ConversationSystem.Tick`: старт, обмен слухами, подслушивание.
7. **Забывание** — `KnowledgeBase.ForgetHourly` раз в игровой час.
8. **Триггеры** — `ProcessTriggersAsync`: важное знание, диалог игрока, ручной вызов.
9. **Метрики** — `MetricsCollector.OnTick`, затем `Clock.Advance`.

События, созданные обработчиками действий (арест, крик, передача, награда), попадают в метрики
через `EventLog.EventAdded` — подписка в конструкторе `Simulation`.

## Потоки и детерминизм

* Симуляция идёт в одном потоке. Промпт собирается синхронно на этом же потоке
  (`PreparedPrompt`), а в фоне выполняется только HTTP — иначе решатель читал бы журнал,
  который в это время меняется.
* `DecisionTaskQueue` ограничивает параллельность `SemaphoreSlim(maxConcurrent)` и складывает
  готовые ответы в очередь; применяются они на шаге 1 следующего тика.
* В `Core` запрещены `Random.Shared`, `Guid.NewGuid()` и текущее время. Вся случайность —
  три генератора: `RngSim`, `RngRumor`, `RngFiller` (seed, seed+1, seed+2).
* Дисторсия тратит ровно три случайных числа на пересказ, чтобы поток `RngRumor` не зависел
  от достоверности: иначе прогоны с разным `trustMultiplier` шли бы по разным разговорам.

## Модель мира

`World` хранит карту, NPC, игрока, часы, генераторы, конфиг, журнал, матрицу доверия, задания,
каталог предметов и предметы на земле.

* Карта 48×32, тайл 24 px, ASCII-карта загружается из `data/maps/default.txt`.
* `zoneOf[x,y]` считается один раз при загрузке; зоны — прямоугольники с соседями.
* A* по 4 соседям: стоимость тайла 1, дорога 0.8, закрытая дверь 1.5.
* NPC движется 1 тайл за тик; при действии «втекти» — 2.

## События и знания

```
WorldEvent → EventLog.Add → (EventAdded) → MetricsCollector
                          → PerceptionSystem.Process → KnowledgeBase.AddFirstHand / MergeIncoming
```

* `WorldEvent` — неизменяемый факт: тип, актор, цель, предмет, зона, координаты, громкость,
  важность, фоновость, `Extra`.
* `KnowledgeEntry` — то, как событие запомнил конкретный NPC: поля могут быть искажены,
  есть источник, достоверность `c`, цепочка пересказчиков, `IsDistorted`, `IsFalse`.
* Формулы: `c = cB · r(A→B)`; независимые подтверждения — шумное ИЛИ
  `1 − (1−a)(1−b)`; забывание `c · exp(−λ)` раз в час.
* Искажения делает игра (`DistortionEngine`), а не модель; модель о пометке не знает.

## Решения V0–V4

| Вариант | Что получает модель | Валидатор | Слова игрока |
|---|---|---|---|
| V0 | нет, правила `RuleDecisionMaker` | да | — |
| V1 | весь журнал | нет | как текст |
| V2 | знания NPC без c | нет | как текст |
| V3 | знания с c и источниками | нет | как текст |
| V4 | как V3 | да | блок «не перевірені» |

`ActionCatalog` читает `data/actions.json` (15 действий, `c_min`, кто может, длительность).
Каждое действие — отдельный класс `IActionHandler`; `ActionExecutor` ведёт текущее действие
NPC и двигает его к цели.

## Конвейер модели

```
DecisionContext
  → PromptBuilder (data/prompts/*.txt, glyph-safe шаблоны)
  → ILlmClient (DeepSeek | Cached | Mock | DryRun)
  → ResponseParser (markdown, мусор, повтор, откат на V0)
  → Validator (6 правил, только V0/V4)
  → ActionExecutor
```

* Кэш: `cache/{sha256}.json`, ключ — модель, температура, system и user.
* Режимы: `Live`, `LiveWithCache`, `ReplayOnly`, `Mock` (`appsettings.json` → `llm.mode`).
* Токены: истина — `usage` из ответа; локальная оценка — `Microsoft.ML.Tokenizers`
  (`o200k_base`) с калибровкой по первым 20 запросам.
* Стоимость — по ценам из конфига, пик/непик по UTC.

## Сценарии и пакетный режим

`ScenarioDefinition` — JSON из `data/scenarios`; `ScenarioLoader` парсит карту, население,
скрипт, overrides и эксперимент. `Simulation` собирает мир и крутит цикл; `ScenarioRunner`
гоняет матрицу «сценарий × вариант × seed × параметры», снимает метрики и пишет CSV
(`BatchOutcome.Write`).

`Svidok.Runner` — оболочка: разбор CLI, прогресс, сводная таблица, код возврата 2 при
401/402.

## Точки расширения

| Что добавить | Куда |
|---|---|
| Новое действие | `data/actions.json` + класс `IActionHandler` + регистрация в `ActionExecutor` + правило в `Validator`, если нужен `eventRef` |
| Новый вариант решений | `Variant`, фабрика в `LlmBootstrap`, флаги в `PromptBuilder` |
| Новый сценарий | JSON в `data/scenarios`; если нужна своя логика — ветка в `ScenarioRunner.RunSingleAsync` |
| Новое событие мира | `data/events.json` (тип, видимость, громкость, важность, `distortable`) |
| Новый промпт-блок | файл в `data/prompts` + плейсхолдер в `PromptBuilder` |
