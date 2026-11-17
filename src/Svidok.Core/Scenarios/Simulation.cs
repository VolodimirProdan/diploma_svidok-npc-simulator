using System.Collections.Concurrent;
using System.Text.Json;
using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.Metrics;
using Svidok.Core.Perception;
using Svidok.Core.Quests;
using Svidok.Core.Util;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Scenarios;

/// <summary>Одна симуляция мира: главный цикл, восприятие, слухи, решения, метрики (раздел 4.2).</summary>
public sealed class Simulation : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<DialogRequest> _dialogRequests = new();
    private readonly Dictionary<string, TaskCompletionSource<DecisionResult>> _awaitingDialog = new(StringComparer.Ordinal);
    private readonly HashSet<int> _forcedWitnessEvents = new();
    private readonly List<ScriptedEvent> _script;
    private int _perceivedEventCount;
    private bool _finished;
    private int _dailyEventDay;
    private int _dailyEventDueTick = -1;
    private int? _activeFireId;
    private int _fireHelpTicks;

    public Simulation(
        ScenarioDefinition scenario,
        AppConfig appConfig,
        Variant variant,
        int seed,
        IDecisionMaker? llmMaker = null,
        ActionCatalog? catalog = null,
        MetricsCollector? metrics = null,
        double? trustMultiplier = null,
        double? talkativenessMultiplier = null)
    {
        Scenario = scenario;
        Variant = variant;
        Seed = seed;
        AppConfig = appConfig;
        LlmMaker = llmMaker;
        Catalog = catalog ?? ActionCatalog.LoadDefault();

        var config = appConfig.Simulation.Clone();
        config.ApplyOverrides(scenario.Overrides);
        if (trustMultiplier.HasValue)
        {
            config.TrustMultiplier = trustMultiplier.Value;
        }
        if (talkativenessMultiplier.HasValue)
        {
            config.TalkativenessMultiplier = talkativenessMultiplier.Value;
        }

        var rng = new Rng(seed);
        var rows = scenario.MapRows is { Count: > 0 } ? scenario.MapRows.ToArray() : ScenarioLoader.LoadMap(scenario.MapId);
        var map = scenario.Zones.Count > 0
            ? GameMap.FromAscii(rows, scenario.Zones
                .Select(z => new Zone(z.Id, z.NameUk, z.X0, z.Y0, z.X1, z.Y1, z.Neighbours.ToArray()))
                .ToList())
            : GameMap.FromAscii(rows);
        var schema = EventSchema.LoadDefault();
        var log = new EventLog(schema);

        var populationData = PopulationFactory.LoadData(scenario.Population);
        var population = new PopulationFactory(populationData);
        var npcs = population.Build(scenario.ExtraNpcs, map, rng);
        PopulationFactory.ApplySpecialSchedules(npcs);

        var spawnScript = scenario.Script
            .Where(s => string.Equals(s.Type, "spawn", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.Actor))
            .Select(s => s.Actor!)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var npc in npcs)
        {
            if (spawnScript.Contains(npc.Id))
            {
                npc.SpawnTick = int.MaxValue;
            }
        }

        var player = new Player
        {
            X = scenario.Player.X,
            Y = scenario.Player.Y,
            Coins = scenario.Player.Coins,
            HomeZone = scenario.Player.Home ?? string.Empty
        };
        player.RenderX = player.X;
        player.RenderY = player.Y;
        foreach (var item in scenario.Player.Inventory)
        {
            player.Inventory.Add(item);
        }

        var clock = new GameClock(scenario.Start.MinuteOfDay);
        PlaceNpcsBySchedule(npcs, map, clock, rng);
        var trust = new TrustMatrix();
        trust.Build(npcs, config, population.Friends, config.TrustMultiplier, includePlayer: true);

        World = new World(map, npcs, player, clock, rng, config, log, trust);
        World.Game.Social = scenario.Features.Social;
        World.Game.Combat = scenario.Features.Combat;
        World.Game.Life = scenario.Features.Life;
        LoadItems(populationData);
        PlaceItems(map, rng);
        BuildQuests();
        if (World.Game.AnyEnabled)
        {
            GameMechanics.AssignBeds(World);
        }
        if (World.Game.Social)
        {
            foreach (var npc in World.Npcs)
            {
                if (World.Trust.Get(npc.Id, Player.PlayerId) < GameRules.NewcomerTrust)
                {
                    World.Trust.Set(npc.Id, Player.PlayerId, GameRules.NewcomerTrust);
                }
                foreach (var relative in npc.Family.Keys)
                {
                    if (string.CompareOrdinal(npc.Id, relative) < 0)
                    {
                        World.Game.Friends.Add((npc.Id, relative));
                    }
                }
            }
            World.Game.Friends.AddRange(population.Friends);
            World.Game.HeadmanId = World.Npcs.FirstOrDefault(n => n.Role == Role.Headman)?.Id;
            foreach (var storyId in Scenario.Stories)
            {
                if (StoryEngine.Load(storyId) is { } story)
                {
                    World.Game.Stories.Add(new StoryState(story));
                }
            }
        }

        Distortion = new DistortionEngine(config, schema);
        Perception = new PerceptionSystem(schema);
        Filler = new FillerGenerator(schema);
        Conversations = new ConversationSystem(config, Distortion);
        Executor = new ActionExecutor(World, Catalog);
        Validator = new Validator(World, Catalog);
        Rules = new RuleDecisionMaker(World, Catalog);
        Queue = new DecisionTaskQueue(Math.Max(1, appConfig.Llm.MaxConcurrent));
        Metrics = metrics ?? new MetricsCollector(scenario, variant, seed, string.Empty);
        Metrics.TrustMult = config.TrustMultiplier;
        Metrics.TalkMult = config.TalkativenessMultiplier;
        Metrics.Start();
        log.EventAdded += worldEvent => Metrics.OnEvent(worldEvent);

        _script = scenario.Script
            .Where(s => !string.Equals(s.Type, "spawn", StringComparison.OrdinalIgnoreCase))
            .OrderBy(s => s.Tick)
            .ToList();
    }

    /// <summary>
    /// Сценарий может стартовать не в 06:00. Тогда NPC должны быть уже на работе или в корчме,
    /// а не «спать» дома: ставим занятие из расписания и переносим в нужную зону.
    /// </summary>
    private static void PlaceNpcsBySchedule(List<Npc> npcs, GameMap map, GameClock clock, Rng rng)
    {
        foreach (var npc in npcs)
        {
            if (npc.SpawnTick > clock.Tick)
            {
                continue;
            }

            var entry = npc.CurrentScheduleEntry(clock.MinuteOfDay);
            if (entry is null)
            {
                continue;
            }

            npc.Activity = entry.Activity;
            if (string.IsNullOrWhiteSpace(entry.ZoneId) || !map.ZoneHasWalkableTile(entry.ZoneId))
            {
                continue;
            }

            var tile = map.RandomTileIn(entry.ZoneId, rng.Sim);
            npc.X = tile.X;
            npc.Y = tile.Y;
            npc.RenderX = tile.X;
            npc.RenderY = tile.Y;
        }
    }

    public ScenarioDefinition Scenario { get; }

    public Variant Variant { get; }

    public int Seed { get; }

    public AppConfig AppConfig { get; }

    public World World { get; }

    public ActionCatalog Catalog { get; }

    public ActionExecutor Executor { get; }

    public Validator Validator { get; }

    public RuleDecisionMaker Rules { get; }

    public IDecisionMaker? LlmMaker { get; set; }

    /// <summary>
    /// Окно игры: что NPC помнит об игроке (имя, память, доверие, слухи, манера речи).
    /// В пакетных прогонах не задаётся, и промпт остаётся прежним.
    /// </summary>
    public Func<Npc, InteractiveDialog?>? InteractiveProvider { get; set; }

    public DecisionTaskQueue Queue { get; }

    public MetricsCollector Metrics { get; }

    /// <summary>Последние вызовы модели с полными промптами — для интерфейса.</summary>
    public List<DecisionTrace> Traces { get; } = new();

    public DistortionEngine Distortion { get; }

    public PerceptionSystem Perception { get; }

    public FillerGenerator Filler { get; }

    public ConversationSystem Conversations { get; }

    /// <summary>Сборка промпта на потоке симуляции (ставится из Svidok.Llm).</summary>
    public Func<DecisionContext, PreparedPrompt>? PromptFactory { get; set; }

    /// <summary>Сборка промпта V4 для метрик S4 (без отправки).</summary>
    public Func<Npc, string>? NpcPromptBuilder { get; set; }

    /// <summary>Локальный счётчик токенов (Microsoft.ML.Tokenizers), ставится из Svidok.Llm.</summary>
    public Func<string, int>? TextTokenCounter { get; set; }

    /// <summary>Калибровка счётчика по usage из ответов API.</summary>
    public Action<int, int>? TokenCalibrationObserver { get; set; }

    /// <summary>Наблюдатель за тиками: прогресс пакетного прогона в консоли.</summary>
    public Action<Simulation>? TickObserver { get; set; }

    /// <summary>fast_forward: решения только по правилам, модель не вызывается.</summary>
    public bool NoLlmMode { get; set; }

    public bool Finished => _finished || (!ContinueAfterEnd && ScenarioOver);

    /// <summary>Время сценария вышло (метрики сценария считаются до этого тика).</summary>
    public bool ScenarioOver => World.Clock.Tick >= Scenario.DurationTicks;

    /// <summary>Интерактивный режим: мир продолжает жить после конца сценария.</summary>
    public bool ContinueAfterEnd { get; private set; }

    public void ContinueBeyondScenario()
    {
        ContinueAfterEnd = true;
        _finished = false;
    }

    public int PendingDecisions => Queue.Pending;

    public int ActiveRequests => Queue.Active;

    public int EstimateTokens(string text) => TextTokenCounter?.Invoke(text) ?? Math.Max(1, text.Length / 4);

    public Task TickAsync(CancellationToken ct) => TickAsync(ct, waitForDecisions: World.Config.WaitForLlm);

    public async Task TickAsync(CancellationToken ct, bool waitForDecisions)
    {
        if (_finished)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        var token = linked.Token;

        // 1. применить готовые решения модели
        while (Queue.TryDequeue(out var completed))
        {
            ApplyPending(completed);
        }

        if (waitForDecisions && Queue.Pending > 0)
        {
            await WaitPendingAsync(token).ConfigureAwait(false);
            while (Queue.TryDequeue(out var completed))
            {
                ApplyPending(completed);
            }
        }

        // 2. события сценария
        RunScript();

        // 2b. ежедневное случайное событие (S4) и состояние пожара (S5)
        UpdateDailyEvents();
        UpdateFire();

        // 3. фоновые события
        if (World.Clock.IsNewHour)
        {
            var added = Filler.GenerateHour(World);
            foreach (var worldEvent in added)
            {
            }
        }

        // 4. движение
        MovementSystem.Update(this);

        // 4b. игровые механики S6–S8: находки, обещания, здоровье, голод, комендантский час
        if (World.Game.AnyEnabled)
        {
            GameMechanics.Tick(this);
        }

        // 5. фильтр восприятия
        Perceive();

        // 6. разговоры и обмен слухами
        Conversations.Tick(World);

        // 7. забывание раз в игровой час
        if (World.Clock.IsNewHour)
        {
            foreach (var npc in World.Npcs)
            {
                npc.Knowledge.ForgetHourly(World.Config.LambdaRumorPerHour, World.Config.LambdaFirstHandPerHour);
            }
        }

        // 8. триггеры решений
        await ProcessTriggersAsync(token).ConfigureAwait(false);

        // 9. метрики
        Metrics.OnTick(this);

        TickObserver?.Invoke(this);

        World.Clock.Advance();
        if (!ContinueAfterEnd && World.Clock.Tick >= Scenario.DurationTicks)
        {
            _finished = true;
        }
    }

    /// <summary>Обработка запросов без хода времени (пауза, диалоги в окне).</summary>
    public async Task PumpAsync(CancellationToken ct)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _cts.Token);
        await ProcessTriggersAsync(linked.Token).ConfigureAwait(false);
        while (Queue.TryDequeue(out var completed))
        {
            ApplyPending(completed);
        }
    }

    public void RunToEnd(CancellationToken ct, bool waitForDecisions)
    {
        while (!Finished)
        {
            TickAsync(ct, waitForDecisions).GetAwaiter().GetResult();
            ct.ThrowIfCancellationRequested();
        }
    }

    public async Task RunToEndAsync(CancellationToken ct, bool waitForDecisions)
    {
        while (!Finished)
        {
            await TickAsync(ct, waitForDecisions).ConfigureAwait(false);
        }
    }

    public async Task RunUntilAsync(int tick, CancellationToken ct, bool waitForDecisions)
    {
        while (World.Clock.Tick < tick && !Finished)
        {
            await TickAsync(ct, waitForDecisions).ConfigureAwait(false);
        }
    }

    public async Task FastForwardAsync(int ticks, CancellationToken ct)
    {
        var wasNoLlm = NoLlmMode;
        NoLlmMode = true;
        try
        {
            for (var i = 0; i < ticks && !Finished; i++)
            {
                await TickAsync(ct, waitForDecisions: true).ConfigureAwait(false);
            }
        }
        finally
        {
            NoLlmMode = wasNoLlm;
        }
    }

    public Task<DecisionResult> AskNpcAsync(Npc npc, string text, IReadOnlyList<DialogLine> history)
    {
        ClaimExtractor.Process(World, npc, text);
        if (World.Game.Social)
        {
            StoryEngine.ProcessSlander(World, npc, text);
        }
        return EnqueueDialog(npc, text, history, Trigger.PlayerDialog, null);
    }

    /// <summary>Житель сам начинает разговор: пояснение для модели, без разбора «слов игрока».</summary>
    public Task<DecisionResult> AskNpcOpeningAsync(Npc npc, string narration, IReadOnlyList<DialogLine> history)
    {
        return EnqueueDialog(npc, narration, history, Trigger.PlayerDialog, null);
    }

    public Task<DecisionResult> AskQuestionAsync(Npc npc, string question)
    {
        return EnqueueDialog(npc, question, Array.Empty<DialogLine>(), Trigger.ScenarioAsk, question);
    }

    private Task<DecisionResult> EnqueueDialog(Npc npc, string text, IReadOnlyList<DialogLine> history, Trigger trigger, string? question)
    {
        var tcs = new TaskCompletionSource<DecisionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _awaitingDialog[npc.Id] = tcs;
        _dialogRequests.Enqueue(new DialogRequest(npc.Id, text, history.ToList(), trigger, question));
        return tcs.Task;
    }

    public void RequestManual(Npc npc) => npc.ManualRequested = true;

    /// <summary>Пакетный режим: решение запрашивается сразу, без ожидания следующего тика.</summary>
    public async Task<DecisionResult> AskNowAsync(
        Npc npc,
        string text,
        Trigger trigger,
        string? question,
        IReadOnlyList<DialogLine> history,
        CancellationToken ct)
    {
        var ctx = new DecisionContext(World, npc, trigger, text, history)
        {
            Situation = question ?? (trigger == Trigger.PlayerDialog ? "Гравець говорить з тобою." : text),
            Question = question
        };

        npc.PendingDecision = true;
        npc.LastDecisionTick = World.Clock.Tick;
        DecisionResult result;
        if (!Variants.UsesLlm(Variant) || NoLlmMode || LlmMaker is null)
        {
            result = await Rules.DecideAsync(ctx, ct).ConfigureAwait(false);
        }
        else
        {
            result = await LlmMaker.DecideAsync(ctx, ct).ConfigureAwait(false);
        }
        npc.PendingDecision = false;
        ApplyDecision(npc, result, trigger, World.Clock.Tick);
        return result;
    }

    /// <summary>Игрок отдаёт предмет NPC: give_item, затем quest_completed, если это предмет задания.</summary>
    public Quest? PlayerGiveItem(string itemId, Npc target)
    {
        if (!World.Player.RemoveItem(itemId))
        {
            return null;
        }

        var tick = World.Clock.Tick;
        var zone = World.ZoneOfTile(World.Player.X, World.Player.Y);
        var give = World.Log.Add(tick, "give_item", Player.PlayerId, target.Id, itemId, zone,
            World.Player.X, World.Player.Y, 0.5, 0.4, false);
        target.Inventory.Add(itemId);
        target.Knowledge.AddFirstHand(give, Source.Participant, 1.0, false, tick, target.Id);

        var quest = World.Quests.All.Values.FirstOrDefault(q =>
            string.Equals(q.Kind == QuestKind.Deliver ? q.RecipientId : q.GiverId, target.Id, StringComparison.Ordinal) &&
            string.Equals(q.RequiredItem, itemId, StringComparison.Ordinal) &&
            q.State == QuestState.Taken);
        if (quest is null || !World.Quests.TryComplete(quest.Id))
        {
            return quest;
        }

        var completed = World.Log.Add(tick, "quest_completed", Player.PlayerId, target.Id, null, zone,
            World.Player.X, World.Player.Y, 0.5, 0.7, false,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["questId"] = quest.Id });
        target.Knowledge.AddFirstHand(completed, Source.Participant, 1.0, false, tick, target.Id);
        if (quest.AutoReward)
        {
            GameMechanics.PayErrand(World, quest);
        }
        return quest;
    }

    /// <summary>Сброс состояния диалога перед атакой S3.</summary>
    public void ResetPlayerDialog(Npc npc)
    {
        npc.Knowledge.RemoveBySource(Source.PlayerClaim);
        World.Trust.Set(npc.Id, Player.PlayerId, World.Config.TrustDefaults.Player);
    }

    public WorldEvent EmitManualEvent(string type, string? actorId, string? targetId, string? itemId, string? zoneId, int x, int y,
        Dictionary<string, string>? extra = null)
    {
        var def = World.Schema.Get(type);
        var worldEvent = World.Log.Add(World.Tick, type, actorId, targetId, itemId,
            zoneId ?? World.ZoneOfTile(x, y), x, y, def.Loudness, def.Importance, def.Background, extra);
        return worldEvent;
    }

    private void ApplyPending(CompletedDecision completed)
    {
        var npc = World.Npc(completed.NpcId);
        if (npc is null)
        {
            return;
        }
        ApplyDecision(npc, completed.Result, Trigger.ImportantKnowledge, completed.Tick);
    }

    private async Task WaitPendingAsync(CancellationToken ct)
    {
        var deadline = Environment.TickCount64 + Math.Max(1000, AppConfig.Llm.TimeoutSec * 1000L);
        while (Queue.Pending > 0 && Environment.TickCount64 < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(5, ct).ConfigureAwait(false);
        }
        await Queue.WaitAllAsync().ConfigureAwait(false);
    }

    private void RunScript()
    {
        for (var i = _script.Count - 1; i >= 0; i--)
        {
            var scripted = _script[i];
            if (scripted.Tick != World.Clock.Tick)
            {
                continue;
            }
            _script.RemoveAt(i);
            ExecuteScripted(scripted);
        }
    }

    private void ExecuteScripted(ScriptedEvent scripted)
    {
        var type = scripted.Type ?? string.Empty;
        if (type.Equals("spawn", StringComparison.OrdinalIgnoreCase))
        {
            SpawnNpc(scripted.Actor);
            return;
        }
        if (type.Equals("set_param", StringComparison.OrdinalIgnoreCase))
        {
            ApplyParam(scripted.Param, scripted.Value);
            return;
        }
        if (GameMechanics.ExecuteScripted(this, scripted))
        {
            return;
        }
        if (type.Equals("fast_forward", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("ask", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("attack_dialog", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var actorNpc = World.Npc(scripted.Actor);
        var position = actorNpc is not null
            ? actorNpc.Position
            : World.Map.RandomTileIn(scripted.Zone, World.Rng.Sim);

        var def = World.Schema.Get(type);
        var worldEvent = World.Log.Add(
            World.Clock.Tick,
            type,
            scripted.Actor,
            scripted.Target,
            scripted.Item,
            scripted.Zone ?? World.ZoneOfTile(position.X, position.Y),
            position.X,
            position.Y,
            def.Loudness,
            def.Importance,
            def.Background,
            scripted.Extra);


        if (scripted.ForcedWitnesses is { Count: > 0 })
        {
            _forcedWitnessEvents.Add(worldEvent.Id);
            ApplyForcedWitnesses(worldEvent, scripted.ForcedWitnesses);
        }

        if (string.Equals(type, "fire", StringComparison.OrdinalIgnoreCase))
        {
            if (scripted.Extra is null || !scripted.Extra.ContainsKey("state"))
            {
                _activeFireId = worldEvent.Id;
                _fireHelpTicks = 0;
            }
        }
    }

    /// <summary>S4: раз в игровые сутки случайное важное событие.</summary>
    private void UpdateDailyEvents()
    {
        if (!Scenario.DailyRandomEvent)
        {
            return;
        }

        var day = World.Clock.Day;
        if (_dailyEventDay == 0 || day != _dailyEventDay)
        {
            _dailyEventDay = day;
            _dailyEventDueTick = World.Clock.Tick + World.Rng.NextInt(120, 1200, World.Rng.Filler);
        }

        if (_dailyEventDueTick < 0 || World.Clock.Tick < _dailyEventDueTick)
        {
            return;
        }
        _dailyEventDueTick = -1;
        EmitDailyEvent();
    }

    private void EmitDailyEvent()
    {
        var types = new[] { "theft", "shout", "fire" };
        var type = types[World.Rng.NextInt(0, types.Length, World.Rng.Filler)];
        var npcs = World.ActiveNpcs().ToList();
        if (npcs.Count == 0)
        {
            return;
        }

        var actor = npcs[World.Rng.NextInt(0, npcs.Count, World.Rng.Filler)];
        switch (type)
        {
            case "fire":
            {
                var zones = new[] { "forge", "tavern", "market", "warehouse" };
                var zoneId = zones[World.Rng.NextInt(0, zones.Length, World.Rng.Filler)];
                var tile = World.Map.RandomTileIn(zoneId, World.Rng.Filler);
                var fireEvent = World.Log.Add(World.Clock.Tick, "fire", null, null, null, zoneId,
                    tile.X, tile.Y, 2.0, 1.0, false);
                _activeFireId = fireEvent.Id;
                _fireHelpTicks = 0;
                break;
            }
            case "shout":
            {
                var shoutEvent = World.Log.Add(World.Clock.Tick, "shout", actor.Id, null, null,
                    World.ZoneOfTile(actor.X, actor.Y), actor.X, actor.Y, 2.0, 0.6, false,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["text"] = "Допоможіть!" });
                break;
            }
            default:
            {
                var victim = npcs[World.Rng.NextInt(0, npcs.Count, World.Rng.Filler)];
                var item = victim.Inventory.Count > 0
                    ? victim.Inventory[World.Rng.NextInt(0, victim.Inventory.Count, World.Rng.Filler)]
                    : "silver_cup";
                var theftEvent = World.Log.Add(World.Clock.Tick, "theft", actor.Id, victim.Id, item,
                    World.ZoneOfTile(victim.X, victim.Y), victim.X, victim.Y, 0.3, 0.9, false);
                break;
            }
        }
    }

    /// <summary>S5: огонь горит 120 тиков, если 3 NPC не тушат рядом; иначе гаснет за 40 тиков.</summary>
    private void UpdateFire()
    {
        if (_activeFireId is not int fireId)
        {
            return;
        }
        var fireEvent = World.Log.ById(fireId);
        if (fireEvent is null)
        {
            _activeFireId = null;
            return;
        }

        var zone = World.Zone(fireEvent.ZoneId);
        var center = zone is null ? (fireEvent.X, fireEvent.Y) : (zone.CenterX, zone.CenterY);
        var helpers = 0;
        foreach (var npc in World.ActiveNpcs())
        {
            if (npc.CurrentAction is null || !string.Equals(npc.CurrentAction.ActionId, "help_fire", StringComparison.Ordinal))
            {
                continue;
            }
            if (Pathfinding.Euclidean(npc.Position, center) <= 8.0)
            {
                helpers++;
            }
        }

        if (helpers >= 3)
        {
            _fireHelpTicks++;
            if (_fireHelpTicks >= 40)
            {
                ExtinguishFire(fireEvent, "out");
            }
            return;
        }

        if (World.Clock.Tick - fireEvent.Tick >= 120)
        {
            ExtinguishFire(fireEvent, "burnt");
        }
    }

    private void ExtinguishFire(WorldEvent fireEvent, string state)
    {
        var finished = World.Log.Add(World.Clock.Tick, "fire", fireEvent.ActorId, null, null, fireEvent.ZoneId,
            fireEvent.X, fireEvent.Y, 1.0, 1.0, false,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["state"] = state });
        _activeFireId = null;
        _fireHelpTicks = 0;
    }

    private void ApplyForcedWitnesses(WorldEvent worldEvent, IEnumerable<string> witnessIds)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in witnessIds)
        {
            var npc = World.Npc(id);
            if (npc is null || !seen.Add(id))
            {
                continue;
            }
            npc.Knowledge.AddFirstHand(worldEvent, Source.Witnessed, 1.0, false, World.Clock.Tick, npc.Id);
        }

        foreach (var participantId in new[] { worldEvent.ActorId, worldEvent.TargetId })
        {
            var npc = World.Npc(participantId);
            if (npc is null || !seen.Add(npc.Id))
            {
                continue;
            }
            npc.Knowledge.AddFirstHand(worldEvent, Source.Participant, 1.0, false, World.Clock.Tick, npc.Id);
        }
    }

    private void SpawnNpc(string? npcId)
    {
        var npc = World.Npc(npcId);
        if (npc is null)
        {
            return;
        }
        var tile = World.Map.RandomTileIn("square", World.Rng.Sim);
        npc.X = tile.X;
        npc.Y = tile.Y;
        npc.RenderX = tile.X;
        npc.RenderY = tile.Y;
        npc.SpawnTick = World.Clock.Tick;
    }

    private void ApplyParam(string? param, string? value)
    {
        if (string.IsNullOrWhiteSpace(param) || string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        var property = typeof(SimulationConfig).GetProperty(param,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase);
        if (property is null || !property.CanWrite)
        {
            Log.Warn("set_param: невідомий параметр " + param);
            return;
        }
        try
        {
            object? parsed = property.PropertyType == typeof(bool)
                ? bool.Parse(value)
                : property.PropertyType == typeof(int)
                    ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture)
                    : double.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
            property.SetValue(World.Config, parsed);
        }
        catch (Exception ex)
        {
            Log.Warn("set_param " + param + ": " + ex.Message);
        }
    }

    private void Perceive()
    {
        if (_perceivedEventCount >= World.Log.Count)
        {
            return;
        }

        var newEvents = new List<WorldEvent>();
        for (var i = _perceivedEventCount; i < World.Log.Count; i++)
        {
            var worldEvent = World.Log.Events[i];
            if (_forcedWitnessEvents.Contains(worldEvent.Id))
            {
                continue;
            }
            newEvents.Add(worldEvent);
        }
        _perceivedEventCount = World.Log.Count;

        Perception.Process(World, newEvents);
        Perception.UpdateSeen(World);
    }

    private async Task ProcessTriggersAsync(CancellationToken ct)
    {
        while (_dialogRequests.TryDequeue(out var request))
        {
            var npc = World.Npc(request.NpcId);
            if (npc is null)
            {
                continue;
            }
            await DispatchAsync(npc, request.Trigger, request.Text, request.History, null, request.Question, ct)
                .ConfigureAwait(false);
        }

        foreach (var npc in World.Npcs)
        {
            if (!npc.ManualRequested || npc.SpawnTick > World.Clock.Tick)
            {
                continue;
            }
            npc.ManualRequested = false;
            if (npc.PendingDecision)
            {
                continue;
            }
            await DispatchAsync(npc, Trigger.Manual, null, Array.Empty<DialogLine>(), null, null, ct)
                .ConfigureAwait(false);
        }

        foreach (var npc in World.Npcs)
        {
            if (npc.PendingDecision || npc.SpawnTick > World.Clock.Tick || npc.KnockedOutUntilTick > World.Clock.Tick)
            {
                continue;
            }
            if (World.Clock.Tick - npc.LastDecisionTick < World.Config.TriggerCooldownTicks)
            {
                continue;
            }

            KnowledgeEntry? triggerEntry = null;
            foreach (var entry in npc.Knowledge.Entries)
            {
                if (entry.UpdatedTick != World.Clock.Tick)
                {
                    continue;
                }
                if (entry.Importance < World.Config.TriggerImportance || entry.C < World.Config.TriggerMinC)
                {
                    continue;
                }
                if (triggerEntry is null || entry.Importance * entry.C > triggerEntry.Importance * triggerEntry.C)
                {
                    triggerEntry = entry;
                }
            }

            if (triggerEntry is null)
            {
                continue;
            }

            var situation = "Ти щойно дізнався про подію #" + triggerEntry.PromptRef + ".";
            await DispatchAsync(npc, Trigger.ImportantKnowledge, null, Array.Empty<DialogLine>(), triggerEntry, null, ct)
                .ConfigureAwait(false);
        }
    }

    private async Task DispatchAsync(
        Npc npc,
        Trigger trigger,
        string? playerText,
        IReadOnlyList<DialogLine> history,
        KnowledgeEntry? focusEntry,
        string? question,
        CancellationToken ct)
    {
        var ctx = new DecisionContext(World, npc, trigger, playerText, history)
        {
            Situation = question is not null
                ? question
                : playerText is not null
                    ? "Гравець говорить з тобою."
                    : focusEntry is not null
                        ? "Ти щойно дізнався про подію #" + focusEntry.PromptRef + "."
                        : "Звичайний день у селищі.",
            FocusEventId = focusEntry?.EventId,
            Question = question,
            Interactive = trigger == Trigger.PlayerDialog ? InteractiveProvider?.Invoke(npc) : null
        };

        npc.PendingDecision = true;
        npc.LastDecisionTick = World.Clock.Tick;

        if (!Variants.UsesLlm(Variant) || NoLlmMode || LlmMaker is null)
        {
            var ruleResult = await Rules.DecideAsync(ctx, ct).ConfigureAwait(false);
            npc.PendingDecision = false;
            ApplyDecision(npc, ruleResult, trigger, World.Clock.Tick);
            return;
        }

        var maker = LlmMaker;
        PreparedPrompt? prepared = null;
        if (maker is IPreparedDecisionMaker preparedMaker && PromptFactory is not null)
        {
            prepared = PromptFactory(ctx);
        }

        var waitForAnswer = World.Config.WaitForLlm;
        if (waitForAnswer)
        {
            var result = prepared is not null && maker is IPreparedDecisionMaker pm
                ? await pm.DecidePreparedAsync(prepared, ctx, ct).ConfigureAwait(false)
                : await maker.DecideAsync(ctx, ct).ConfigureAwait(false);
            npc.PendingDecision = false;
            ApplyDecision(npc, result, trigger, World.Clock.Tick);
            return;
        }

        Queue.Enqueue(npc.Id, World.Clock.Tick, token =>
        {
            if (prepared is not null && maker is IPreparedDecisionMaker pm)
            {
                return pm.DecidePreparedAsync(prepared, ctx, token);
            }
            return maker.DecideAsync(ctx, token);
        }, _cts.Token);
    }

    private void ApplyDecision(Npc npc, DecisionResult result, Trigger trigger, int decisionTick)
    {
        npc.PendingDecision = false;

        var proposed = DecisionNormalizer.Normalize(World, result.Proposed ?? LlmDecision.Ignore());
        var validation = ValidationResult.Success;
        var executed = proposed;

        if (Variants.UsesValidator(Variant))
        {
            validation = Validator.Validate(npc, proposed);
            if (!validation.Ok)
            {
                var say = string.IsNullOrWhiteSpace(proposed.Say)
                    ? "Мені треба в цьому переконатися."
                    : proposed.Say;
                executed = new LlmDecision("say", Target: proposed.Target, Say: say,
                    Reason: "validator: " + RejectReasons.NameUk(validation.Reason!.Value));
            }
        }
        else if (!Catalog.Contains(proposed.Action))
        {
            executed = LlmDecision.Ignore(reason: "невідома дія " + proposed.Action);
        }

        // начатый обыск или задержание не сбивается пустым решением «ничего не делать / сказать»
        var keepsDuty = World.Game.Social && npc.CurrentAction is { ActionId: "search" or "arrest" } &&
                        executed.Action is "ignore" or "say" or "refuse";
        if (!keepsDuty)
        {
            Executor.Execute(npc, executed);
        }

        if (World.Game.Social && executed.EventRef is int secretRef && npc.Secrets.ContainsKey(secretRef) &&
            executed.Action is "warn" or "report_to_guard" or "shout_alarm")
        {
            World.Game.PromiseBreaks++;
        }

        if (!string.IsNullOrWhiteSpace(executed.Say))
        {
            npc.LastSay = executed.Say.Length > 200 ? executed.Say[..200] : executed.Say;
            npc.LastSayTick = World.Clock.Tick;
        }

        var cost = result.Llm is null
            ? 0
            : CostCalculator.Compute(AppConfig.Llm.Prices, result.Llm.CacheHitTokens, result.Llm.CacheMissTokens,
                result.Llm.CompletionTokens, DateTime.UtcNow);

        if (Variants.UsesLlm(Variant) || result.Llm is not null)
        {
            Metrics.RecordLlmCall(decisionTick, npc.Id, trigger.ToString(), executed, validation,
                result.ParseError, result.PromptTokensLocal, result, result.Llm?.CacheFile ?? string.Empty, cost);

            Traces.Add(new DecisionTrace(
                decisionTick,
                npc.Id,
                npc.Name,
                Triggers.NameUk(trigger),
                executed.Action,
                validation.Ok ? "виконано" : "відхилено",
                validation.Reason is RejectReason reject ? RejectReasons.NameUk(reject) : string.Empty,
                result.ParseError,
                result.PromptText,
                result.RawResponse,
                result.Llm?.PromptTokens ?? 0,
                result.Llm?.CompletionTokens ?? 0,
                result.Llm?.LatencyMs ?? 0,
                cost,
                result.Llm?.FromCache ?? false));
            if (Traces.Count > 300)
            {
                Traces.RemoveRange(0, Traces.Count - 300);
            }
        }

        if (result.Llm is not null && result.PromptTokensLocal > 0 && result.Llm.PromptTokens > 0)
        {
            TokenCalibrationObserver?.Invoke(result.PromptTokensLocal, result.Llm.PromptTokens);
        }

        if (_awaitingDialog.Remove(npc.Id, out var tcs))
        {
            tcs.TrySetResult(result with
            {
                Executed = executed,
                Validation = validation
            });
        }
    }

    private void LoadItems(PopulationData population)
    {
        var path = PathResolver.ResolveExisting("data/items.json");
        if (!File.Exists(path))
        {
            return;
        }
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip };
        var items = JsonSerializer.Deserialize<List<ItemDef>>(File.ReadAllText(path), options) ?? new List<ItemDef>();
        foreach (var item in items)
        {
            // игровые предметы не попадают в эксперименты S1–S5: ни в мир, ни в промпт
            if (item.Game && !World.Game.AnyEnabled)
            {
                continue;
            }
            World.Items[item.Id] = item;
        }
    }

    private void PlaceItems(GameMap map, Rng rng)
    {
        foreach (var item in World.Items.Values)
        {
            if (item.Game)
            {
                continue;
            }
            PlaceItem(item, item.Location, item.OwnerId, map, rng);
        }

        foreach (var placement in Scenario.Items)
        {
            if (!World.Items.TryGetValue(placement.Id, out var item))
            {
                continue;
            }
            PlaceItem(item, placement.Zone ?? placement.Owner ?? item.Location, placement.Owner, map, rng);
        }
    }

    private void PlaceItem(ItemDef item, string? location, string? ownerId, GameMap map, Rng rng)
    {
        if (!string.IsNullOrWhiteSpace(ownerId) && World.Npc(ownerId) is Npc owner)
        {
            if (!owner.Inventory.Contains(item.Id, StringComparer.Ordinal))
            {
                owner.Inventory.Add(item.Id);
            }
            return;
        }

        if (!string.IsNullOrWhiteSpace(location) && World.Npc(location) is Npc holder)
        {
            if (!holder.Inventory.Contains(item.Id, StringComparer.Ordinal))
            {
                holder.Inventory.Add(item.Id);
            }
            return;
        }

        var zoneId = string.IsNullOrWhiteSpace(location) ? "square" : location;
        if (World.Zone(zoneId) is null)
        {
            zoneId = "square";
        }
        var tile = map.RandomTileIn(zoneId, rng.Sim);
        World.GroundItems.Add(new GroundItem(item.Id, tile.X, tile.Y, zoneId));
    }

    private void BuildQuests()
    {
        foreach (var spec in Scenario.Quests)
        {
            var state = spec.InitialState?.Trim().ToLowerInvariant() switch
            {
                "taken" => QuestState.Taken,
                "completed" => QuestState.Completed,
                "rewarded" => QuestState.Rewarded,
                _ => QuestState.NotTaken
            };
            World.Quests.Add(new Quest(spec.Id, spec.Giver, spec.TitleUk, spec.RequiredItem, spec.Reward, state));
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        Queue.Dispose();
        _cts.Dispose();
    }

    private sealed record DialogRequest(
        string NpcId,
        string Text,
        IReadOnlyList<DialogLine> History,
        Trigger Trigger,
        string? Question);
}
