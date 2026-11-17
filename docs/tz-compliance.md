# Соответствие техническому заданию

Проверка по разделам ТЗ «Свідок» 1.0 от 12.11.2026. Колонка «Где» указывает файлы и классы,
колонка «Статус» — что сделано и какие есть замечания.

| Раздел ТЗ | Что требовалось | Где реализовано | Статус |
|---|---|---|---|
| 1. Назначение | 2D-симулятор села, режимы наблюдателя/игрока/пакетный, сравнение V0–V4 | `README.md`, `data/scenarios/*.json`, `Svidok.Runner` | Готово |
| 2. Термины | WorldEvent, EventLog, KnowledgeEntry, c, r, фильтр, валидатор, тик, seed | `Core/Events`, `Core/Knowledge`, `Core/Config` | Готово |
| 3. Платформа | .NET 10, Raylib-cs 7.0.2, rlImgui-cs 3.2.0, System.Text.Json, HttpClient, ML.Tokenizers 2.x + O200kBase, xUnit, Noto Sans | `*.csproj`, `assets/fonts`, `Directory.Build.props` | Готово |
| 4. Архитектура | 5 проектов, зависимости Game/Runner → Core, Llm; Core без Raylib и HTTP; главный цикл из 9 шагов | `Svidok.sln`, `Core/Scenarios/Simulation.cs` | Готово |
| 5.1 Карта | 48×32, тайлы 24 px, ASCII, 11 зон, `zoneOf`, соседи зон | `data/maps/default.txt`, `World/GameMap.cs`, `World/Zone.cs` | Готово |
| 5.2 Время | 1 тик = минута, старт 06:00, скорости ×1–×64, ночь 22:00–05:00 (×0.5, кроме fire) | `World/GameClock.cs`, `Config/AppConfig.cs` | Готово |
| 5.3 Случайность | три `System.Random` (seed, +1, +2), запрет Random.Shared/Guid/времени в Core | `World/Rng.cs` | Готово |
| 5.4 NPC | 24 персонажа, роли, цвета, расписания, A*, движение 1 тайл/тик | `data/population.json`, `Entities/Npc.cs`, `Scenarios/PopulationFactory.cs`, `World/Pathfinding.cs` | Готово |
| 5.5 Игрок | WASD 4 тайла/с, инвентарь, монеты, события от действий | `Entities/Player.cs`, `Game/PlayerController.cs` | Готово |
| 5.6 Предметы | 7 предметов, владельцы, склад/рынок/кузня | `data/items.json`, `World/GroundItem.cs` | Готово |
| 6. Журнал | `WorldEvent`, `ToJsonBytes`, `events.json` (17 типов), фоновый генератор по λ и Activity | `Core/Events/*`, `data/events.json` | Готово |
| 7. Восприятие | зрение/слух, Брезенхэм, блокировка обзора и звука, сон, маска актёра | `Core/Perception/*` | Готово |
| 8. Знания | запись знания, c, цепочки, шумное ИЛИ, забывание, доверие, разговоры, подслушивание, искажения, слова игрока | `Core/Knowledge/*` | Готово |
| 9. Решения | триггеры, V0–V4, каталог 15 действий, правила V0, промпты, парсер, валидатор (6 правил), 15 обработчиков, задания | `Core/Decisions/*`, `Llm/*`, `data/actions.json`, `data/prompts/*` | Готово |
| 10. Клиент | POST /chat/completions, response_format json, usage и cache-токены, retry 429/5xx, 401/402 → стоп, SemaphoreSlim, 4 режима, кэш SHA-256, токены и стоимость, бюджет | `Llm/DeepSeekClient.cs`, `Llm/CachedLlmClient.cs`, `Llm/TokenCounter.cs`, `Core/Metrics/CostCalculator.cs` | Готово |
| 11.1 Меню | карточки сценариев, режим, вариант, seed, режим модели, индикатор ключа | `Game/MenuScreen.cs` | Готово |
| 11.2 Наблюдатель | камера, зум, пауза, скорости, `.`, V/H/T/G/L/R, Ctrl+ЛКМ, Esc | `Game/GameApp.cs`, `Game/SimulationHost.cs` | Готово |
| 11.3 Игрок | WASD, E, F, Q, G, Y, I, Tab, Esc | `Game/PlayerController.cs`, `Game/GameApp.cs` | Готово |
| 11.4 Диалог | 520×420, история, плашка действия, ввод, кнопки, «…», техданные | `Game/DialogWindow.cs` | Готово |
| 11.5 Пакетный режим | `--scenario/--variants/--seeds/--llm/--out/--param/--dry-run`, прогресс, сводка | `Runner/Program.cs`, `Core/Scenarios/ScenarioRunner.cs` | Готово |
| 12. Интерфейс | 1600×900 (мин. 1280×720), верхняя строка, панель 480, нижняя строка, 5 вкладок, тепловая карта, шрифт с кириллицей | `Game/GameApp.cs`, `Game/ObserverPanel.cs`, `Game/WorldRenderer.cs` | Готово |
| 13. Сценарии | S1–S6, S2-big, население, расписания, события, метрики | `data/scenarios/*`, `Core/Scenarios/*` | Готово |
| 14. Метрики | покрытие, T50/T90, средняя c, искажения, ложный подозреваемый, K, MKR, Recall, MSR, FRR, задержки, стоимость, ошибки разбора | `Core/Metrics/MetricsCollector.cs`, `Core/Scenarios/ScenarioRunner.cs` | Готово |
| 15. Выходные файлы | все 11 CSV, разделитель `;`, точка, UTF-8 с BOM, `results/{scenario}_{date}` | `Core/Metrics/CsvWriter.cs`, `BatchOutcome.Write` | Готово |
| 16. Конфигурация | `appsettings.json` со всеми параметрами, ключ только в env | `appsettings.json`, `Core/Config/AppConfig.cs` | Готово |
| 17. Формат сценария | карта, население, extraNpcs, start, player, quests, overrides, script (`ask`, `attack_dialog`, `fast_forward`, `set_param`, `forcedWitnesses`), experiment | `Core/Scenarios/ScenarioDefinition.cs`, `ScenarioLoader.cs` | Готово |
| 18. Требования к коду | nullable, implicit usings, latest, английские имена, Core без графики/HTTP, отдельный класс на действие, лог в `logs/`, числа из конфига | `Directory.Build.props`, `Core/Util/Log.cs` | Готово, замечание: часть подписей UI задана в коде панелей, а не в `UiText` |
| 19. Тесты | 16 обязательных сценариев | `tests/Svidok.Tests` (26 тестов) | Готово |
| 20. Этапы | 8 этапов разработки и приёмки | весь проект | Готово |
| 21. Материалы для диплома | CSV, скриншоты, архитектурные схемы | `results/`, `docs/screenshots/`, `docs/architecture.md` | Готово |
| 22. Риски | dry-run, бюджет, кэш, ReplayOnly, Mock, обработка битого JSON | `--dry-run`, `--budget`, `cache/`, `llm.mode` | Готово |
| Приложение А | 30 атак по 5 категориям | `data/attacks_s3.json` | Готово |
| Приложение Б | порядок первого запуска | `README.md` | Готово |

## Отклонения от буквы ТЗ

1. Пространство имён `Svidok.Core.WorldModel` вместо `Svidok.Core.World`: класс `World`
   и одноимённое пространство имён конфликтуют в C#.
2. `LlmDecisionMaker`, `PromptBuilder`, `ResponseParser`, `TokenCounter` живут в `Svidok.Llm`,
   чтобы `Core` не зависел от HTTP и токенизатора. Контракты `ILlmClient`, `LlmRequest`,
   `LlmResult` — в `Svidok.Core.Llm` как модели данных.
3. Промпт собирается на потоке симуляции, в фоне выполняется только HTTP
   (`PreparedPrompt`), иначе решатель читал бы журнал, который меняется.
4. `llm_calls.csv` пишется только для вариантов с моделью; прогоны V0 в нём не появляются.
5. Замеры S4 в конце суток включаются флагом `--s4-calls` (иначе S4 идёт без модели).
6. Метрика утечки S2 уточнена: отрицание («нічого не чув про кубок») утечкой не считается.
7. Дисторсия тратит фиксированное число случайных чисел, чтобы сравнение по `trustMultiplier`
   шло на одних и тех же разговорах.
8. В S2-big события скрипта стоят после `fast_forward` (тики 2910 и 3135).
9. Правило V0 «арест при c ≥ 0.9» срабатывает один раз на событие.
