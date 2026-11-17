using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using System.Text;
using System.Text.Json;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.Metrics;
using Svidok.Core.Scenarios;
using Svidok.Core.Util;
using Svidok.Core.WorldModel;
using Svidok.Llm;

namespace Svidok.Game;

/// <summary>Одна сессия симуляции: скорости, камера, диалоги, ручные события, экспорт.</summary>
public sealed class SimulationHost : IDisposable
{
    private readonly AppConfig _config;
    private readonly ActionCatalog _catalog;
    private readonly TokenCounter _tokens;
    private readonly int _seed;
    private readonly Variant _variant;
    private double _accumulator;
    private bool _cameraFitted;
    private int _lastLoggedEventIndex;
    private bool _pausedBeforeDialog;
    private readonly PlayerProfile? _profile;
    private readonly ScenarioMemory _memory;
    private readonly Dictionary<string, string> _styles;
    private readonly Dictionary<string, Task<string?>> _compactions = new(StringComparer.Ordinal);
    private int _lastHour = -1;

    /// <summary>Память NPC об игроке растёт заметками; выше этого порога она сжимается фоновым запросом.</summary>
    public const int MemoryCompactAt = 3000;

    /// <summary>Жёсткий потолок памяти одного NPC об игроке, символов.</summary>
    public const int MemoryHardCap = 5000;

    /// <summary>Сколько последних реплик разговора хранится дословно.</summary>
    public const int HistoryLines = 12;

    public SimulationHost(
        ScenarioDefinition scenario,
        AppConfig config,
        ActionCatalog catalog,
        Variant variant,
        int seed,
        bool playerMode,
        TokenCounter tokens,
        PlayerProfile? profile = null)
    {
        _profile = playerMode ? profile : null;
        _memory = _profile?.ForScenario(scenario) ?? new ScenarioMemory();
        _styles = LoadStyles();
        if (_profile is not null)
        {
            _profile.LastPlayed = DateTime.Now;
        }
        Scenario = scenario;
        _config = config;
        _catalog = catalog;
        _variant = variant;
        _seed = seed;
        _tokens = tokens;
        PlayerMode = playerMode;

        Simulation = CreateSimulation();
        Camera = new Camera2D
        {
            Target = new Vector2(24 * 24, 16 * 24),
            Offset = new Vector2(560, 450),
            Rotation = 0,
            Zoom = 1.0f
        };
        ResearcherMode = config.Ui.ResearcherMode;
        Paused = false;
        SpeedIndex = 1;
        SelectedNpc = Simulation.World.Npcs.FirstOrDefault(n => n.Role == Role.Innkeeper);
    }

    public ScenarioDefinition Scenario { get; }

    public Simulation Simulation { get; private set; }

    public bool PlayerMode { get; }

    /// <summary>Имя, под которым игрока знают персонажи.</summary>
    public string PlayerName => _profile?.Name ?? "Мандрівник";

    public PlayerProfile? Profile => _profile;

    /// <summary>Что NPC помнит об игроке (для окна разговора).</summary>
    public NpcMemory MemoryOf(Npc npc) => _memory.For(npc.Id);

    /// <summary>Идёт ли сейчас сжатие памяти этого NPC.</summary>
    public bool IsCompacting(Npc npc) => _compactions.ContainsKey(npc.Id);

    public bool Paused { get; set; }

    public int SpeedIndex { get; set; }

    public Camera2D Camera;

    public Npc? SelectedNpc { get; set; }

    public Npc? HoveredNpc { get; set; }

    public int? SelectedEventId { get; set; }

    /// <summary>Режим снимка: клики по таблицам не меняют выбранное событие.</summary>
    public bool LockEventSelection { get; set; }

    public bool ShowVision { get; set; }

    public bool ShowHearing { get; set; }

    public bool ShowGrid { get; set; }

    public bool ShowNames { get; set; }

    public bool ShowTrust { get; set; }

    public bool ShowHeatmap { get; set; }

    public bool ShowObservedTileInfo { get; set; }

    public bool ResearcherMode { get; set; }

    public bool ShowTechDetails;

    public bool ShowInventory { get; set; }

    public DialogState? Dialog { get; private set; }

    public string? Notice { get; private set; }

    public double NoticeUntil { get; set; }

    public string? LastEventText { get; private set; }

    public double LastEventAt { get; private set; }

    public int LastEventTick { get; private set; } = -1;

    public string? FatalError { get; private set; }

    public bool Finished => Simulation.Finished;

    /// <summary>Окно «сценарій завершено» уже показано и закрыто.</summary>
    public bool EndAcknowledged { get; set; }

    /// <summary>Сколько реплик игрок отправил NPC (для подсказок).</summary>
    public int DialogsSent { get; private set; }

    /// <summary>Сколько реплик отправлено после кражи, совершённой игроком.</summary>
    public int DialogsAfterTheft { get; private set; }

    /// <summary>Игрок говорил с выдавшим задание, не имея нужного предмета.</summary>
    public bool TriedToDeceive { get; private set; }

    /// <summary>Игрок что-то украл.</summary>
    public bool PlayerStole { get; private set; }

    /// <summary>Последняя кража игрока — её по умолчанию показывает тепловая карта.</summary>
    public int? PlayerTheftEventId { get; private set; }

    /// <summary>Последнее заметное событие мира.</summary>
    public int? LastImportantEventId { get; private set; }

    public void ContinueBeyondScenario()
    {
        Simulation.ContinueBeyondScenario();
        EndAcknowledged = true;
        Paused = false;
        if (SpeedIndex == 0)
        {
            SpeedIndex = 1;
        }
        ShowNotice("Сценарій завершено, світ живе далі");
    }

    public float Zoom
    {
        get => Camera.Zoom;
        set => Camera.Zoom = Math.Clamp(value, 0.5f, 3.0f);
    }

    private Simulation CreateSimulation()
    {
        var metrics = new MetricsCollector(Scenario, _variant, _seed, "manual") { Enabled = true };
        var simulation = new Simulation(Scenario, _config, _variant, _seed, null, _catalog, metrics);
        LlmBootstrap.Create(simulation, _variant, _config.Llm, _tokens, dryRun: false);
        _lastLoggedEventIndex = 0;
        _lastHour = -1;

        if (PlayerMode)
        {
            simulation.World.Player.Name = PlayerName;
            simulation.World.Game.PlayerActive = true;
            foreach (var (npcId, known) in _memory.Npcs)
            {
                if (known.Talks > 0)
                {
                    simulation.World.Game.Acquainted.Add(npcId);
                }
            }
            RestorePlayer(simulation);
            simulation.InteractiveProvider = BuildInteractive;
            foreach (var (npcId, memory) in _memory.Npcs)
            {
                // отсчёт «давно не говорили» начинается с загрузки, а не с минус бесконечности
                memory.LastTalkTick = simulation.World.Clock.Tick;
                if (memory.Trust >= 0)
                {
                    simulation.World.Trust.Set(npcId, Player.PlayerId, memory.Trust);
                }
            }
        }
        return simulation;
    }

    public void Restart()
    {
        SaveProfile();
        Simulation.Dispose();
        Simulation = CreateSimulation();
        Dialog = null;
        SelectedEventId = null;
        SelectedNpc = Simulation.World.Npcs.FirstOrDefault();
        Paused = false;
        _accumulator = 0;
        EndAcknowledged = false;
        DialogsSent = 0;
        DialogsAfterTheft = 0;
        TriedToDeceive = false;
        PlayerStole = false;
        PlayerTheftEventId = null;
        LastImportantEventId = null;
        ShowNotice("Симуляцію перезапущено");
    }

    public void ShowNotice(string text)
    {
        Notice = text;
        NoticeUntil = Raylib.GetTime() + 4.0;
    }

    public double VisionRadiusOf(Npc npc)
    {
        var radius = Simulation.World.Config.VisionRadius;
        if (Simulation.World.Clock.IsNight(Simulation.World.Config))
        {
            radius *= Simulation.World.Config.NightVisionFactor;
        }
        return radius;
    }

    public void Update(double dt)
    {
        try
        {
            if (!Paused && !Finished && FatalError is null)
            {
                var ticksPerSecond = SpeedIndex switch
                {
                    0 => 0.0,
                    1 => 2.0,
                    2 => 8.0,
                    3 => 32.0,
                    4 => 128.0,
                    _ => 2.0
                };
                // пока игрок спит, ночь проходит за несколько секунд
                var sleeping = PlayerMode && Simulation.World.Player.IsAsleep(Simulation.World.Tick);
                if (sleeping)
                {
                    ticksPerSecond = Math.Max(ticksPerSecond, 160.0);
                }
                _wasSleeping |= sleeping;
                _accumulator += dt * ticksPerSecond;
                var processed = 0;
                while (_accumulator >= 1.0 && processed < 256 && !Finished)
                {
                    Simulation.TickAsync(CancellationToken.None, waitForDecisions: false).GetAwaiter().GetResult();
                    _accumulator -= 1.0;
                    processed++;
                }
            }

            Simulation.PumpAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        catch (LlmFatalException ex)
        {
            FatalError = ex.Message;
            Paused = true;
        }
        catch (Exception ex)
        {
            FatalError = "Помилка симуляції: " + ex.Message;
            Paused = true;
            Log.Error("Помилка в ігровому циклі", ex);
        }

        if (_wasSleeping && PlayerMode && !Simulation.World.Player.IsAsleep(Simulation.World.Tick))
        {
            _wasSleeping = false;
            ShowNotice("Ви прокинулись. " + Simulation.World.Clock.TimeString + ".");
        }

        UpdateIncomingTalks();
        UpdateDialog();
        UpdateCompactions();
        DriftTrust();
        TrackEvents();
    }

    private bool _wasSleeping;

    /// <summary>Житель дошёл до игрока: открываем разговор, первым говорит он.</summary>
    private void UpdateIncomingTalks()
    {
        var notices = Simulation.World.Game.StoryNotices;
        if (notices.Count > 0 && (Notice is null || Raylib.GetTime() > NoticeUntil - 2.0))
        {
            ShowNotice(notices.Dequeue());
            NoticeUntil = Raylib.GetTime() + 7.0;
        }
        if (!PlayerMode || Dialog is not null)
        {
            return;
        }
        var world = Simulation.World;
        var queue = world.Game.IncomingTalks;
        while (queue.Count > 0)
        {
            var talk = queue.Peek();
            var npc = world.Npc(talk.NpcId);
            if (npc is null || world.Tick - talk.Tick > 30)
            {
                queue.Dequeue();
                continue;
            }
            if (CannotTalkReason(npc) is not null)
            {
                return;
            }
            queue.Dequeue();
            StartIncomingTalk(npc, talk);
            return;
        }
    }

    private void StartIncomingTalk(Npc npc, IncomingTalk talk)
    {
        OpenDialog(npc);
        if (Dialog is null)
        {
            return;
        }
        ShowNotice(npc.Name + Svidok.Core.Util.UkGrammar.G(npc, " підійшов", " підійшла") + " до вас поговорити.");
        npc.LastSay = talk.Opening;
        npc.LastSayTick = Simulation.World.Tick;

        var usesModel = Variants.UsesLlm(Simulation.Variant) && Simulation.LlmMaker is not null && !Simulation.NoLlmMode;
        if (!usesModel)
        {
            Dialog.Lines.Add(new DialogLine(npc.Id, talk.Opening, false));
            Dialog.ScrollToEnd = true;
            return;
        }
        var narration = "(Ти сам підійшов до " + PlayerName + ", він тебе не кликав. " +
                        (string.IsNullOrWhiteSpace(talk.Reason) ? string.Empty : "Навіщо: " + talk.Reason + ". ") +
                        "Почни розмову першим, коротко, своїми словами. Можеш почати так: «" + talk.Opening + "»)";
        Dialog.Pending = Simulation.AskNpcOpeningAsync(npc, narration, Dialog.Lines.ToList());
    }

    // ---------------------------------------------------------------- сон и сохранение

    public void Sleep()
    {
        if (!PlayerMode)
        {
            return;
        }
        if (Dialog is not null)
        {
            CloseDialog();
        }
        var result = GameMechanics.PlayerSleep(Simulation.World);
        ShowNotice(result.Message);
        if (result.Started)
        {
            Paused = false;
        }
    }

    public bool HasSave => _profile is not null && _profile.Saves.ContainsKey(Scenario.Id);

    public SavedPlayer? CurrentSave => _profile is not null && _profile.Saves.TryGetValue(Scenario.Id, out var save) ? save : null;

    /// <summary>
    /// Кнопка «Зберегти»: что о вас помнят жители (доверие, заметки, разговоры) и что у вас есть
    /// (место, монеты, предметы, здоровье, голод). Следующий запуск этого сценария продолжит с этого места.
    /// </summary>
    public void SaveGame()
    {
        if (_profile is null)
        {
            ShowNotice("Щоб зберігати гру, оберіть профіль у меню.");
            return;
        }
        var world = Simulation.World;
        var player = world.Player;
        _profile.Saves[Scenario.Id] = new SavedPlayer
        {
            Saved = DateTime.Now,
            GameTime = world.Clock.TimeString,
            X = player.X,
            Y = player.Y,
            Coins = player.Coins,
            Inventory = player.Inventory.ToList(),
            Hp = Math.Max(1, player.Hp),
            Hunger = player.Hunger,
            Seed = _seed
        };
        SaveProfile();
        var known = _memory.Npcs.Count(n => n.Value.Talks > 0);
        ShowNotice("Гру збережено. Монет: " + player.Coins + ", предметів: " + player.Inventory.Count +
                   ", знайомих: " + known + ".");
    }

    private void RestorePlayer(Simulation simulation)
    {
        if (CurrentSave is not { } save)
        {
            return;
        }
        var world = simulation.World;
        var player = world.Player;
        if (world.Map.IsWalkable(save.X, save.Y))
        {
            player.Teleport(save.X, save.Y);
        }
        player.Coins = save.Coins;
        player.Inventory.Clear();
        foreach (var item in save.Inventory)
        {
            if (world.Items.ContainsKey(item))
            {
                player.Inventory.Add(item);
            }
        }
        player.SelectedIndex = 0;
        player.Hp = Math.Clamp(save.Hp, 1, player.MaxHp);
        player.Hunger = Math.Clamp(save.Hunger, 0, 100);
        ShowNotice("Продовжено зі збереження від " + save.Saved.ToString("dd.MM HH:mm") + ".");
    }

    /// <summary>Начать сценарий с нуля: сохранение места и вещей стирается, память жителей остаётся.</summary>
    public void DeleteSave()
    {
        if (_profile is not null && _profile.Saves.Remove(Scenario.Id))
        {
            SaveProfile();
        }
    }

    private void TrackEvents()
    {
        var log = Simulation.World.Log;
        for (var i = _lastLoggedEventIndex; i < log.Count; i++)
        {
            var worldEvent = log.Events[i];
            if (PlayerMode)
            {
                NoticeForPlayer(worldEvent);
            }
            if (string.Equals(worldEvent.Type, "theft", StringComparison.Ordinal) &&
                string.Equals(worldEvent.ActorId, Player.PlayerId, StringComparison.Ordinal))
            {
                PlayerStole = true;
                PlayerTheftEventId = worldEvent.Id;
            }
            if (worldEvent.IsBackground || worldEvent.Importance < 0.5)
            {
                continue;
            }
            LastImportantEventId = worldEvent.Id;
            LastEventText = Simulation.World.TimeOf(worldEvent.Tick) + " " +
                            Simulation.World.Schema.Get(worldEvent.Type).NameUk +
                            " · " + Simulation.World.ZoneName(worldEvent.ZoneId);
            LastEventAt = Raylib.GetTime();
            LastEventTick = worldEvent.Tick;
        }
        _lastLoggedEventIndex = log.Count;
    }

    public void UpdateCamera(Rectangle viewport)
    {
        var io = ImGui.GetIO();
        Camera.Offset = new Vector2(viewport.X + viewport.Width / 2, viewport.Y + viewport.Height / 2);

        if (!_cameraFitted && viewport.Width > 32 && viewport.Height > 32)
        {
            _cameraFitted = true;
            var fitWidth = Simulation.World.Map.Width * 24f;
            var fitHeight = Simulation.World.Map.Height * 24f;
            var fit = Math.Min(2.5f, Math.Max(viewport.Width / fitWidth, viewport.Height / fitHeight));
            Camera = new Camera2D
            {
                Target = new Vector2(fitWidth / 2f, fitHeight / 2f),
                Offset = Camera.Offset,
                Rotation = 0,
                Zoom = Math.Max(0.5f, fit)
            };
        }

        if (!io.WantCaptureMouse)
        {
            var wheel = Raylib.GetMouseWheelMove();
            if (Math.Abs(wheel) > 0.001f)
            {
                var mouse = Raylib.GetMousePosition();
                var before = Raylib.GetScreenToWorld2D(mouse, Camera);
                Zoom += wheel * 0.15f;
                var after = Raylib.GetScreenToWorld2D(mouse, Camera);
                Camera = new Camera2D
                {
                    Target = Camera.Target + (before - after),
                    Offset = Camera.Offset,
                    Rotation = 0,
                    Zoom = Camera.Zoom
                };
            }

            if (Raylib.IsMouseButtonDown(MouseButton.Right) || Raylib.IsMouseButtonDown(MouseButton.Middle))
            {
                var delta = Raylib.GetMouseDelta();
                Camera = new Camera2D
                {
                    Target = Camera.Target - delta / Camera.Zoom,
                    Offset = Camera.Offset,
                    Rotation = 0,
                    Zoom = Camera.Zoom
                };
            }
        }

        var mapWidth = Simulation.World.Map.Width * 24;
        var mapHeight = Simulation.World.Map.Height * 24;
        Camera = new Camera2D
        {
            Target = new Vector2(
                Math.Clamp(Camera.Target.X, 0, mapWidth),
                Math.Clamp(Camera.Target.Y, 0, mapHeight)),
            Offset = Camera.Offset,
            Rotation = 0,
            Zoom = Camera.Zoom
        };
    }

    public Npc? NpcAtScreen(Vector2 screen)
    {
        var world = Raylib.GetScreenToWorld2D(screen, Camera);
        Npc? best = null;
        var bestDistance = float.MaxValue;
        foreach (var npc in Simulation.World.ActiveNpcs())
        {
            var center = new Vector2((float)(npc.RenderX * 24 + 12), (float)(npc.RenderY * 24 + 12));
            var distance = Vector2.Distance(center, world);
            if (distance <= 14 && distance < bestDistance)
            {
                best = npc;
                bestDistance = distance;
            }
        }
        return best;
    }

    public (int X, int Y) TileAtScreen(Vector2 screen)
    {
        var world = Raylib.GetScreenToWorld2D(screen, Camera);
        return ((int)Math.Floor(world.X / 24), (int)Math.Floor(world.Y / 24));
    }

    /// <summary>Можно ли сейчас заговорить: без сознания, обиделся, сам игрок в отключке.</summary>
    public string? CannotTalkReason(Npc npc)
    {
        var world = Simulation.World;
        var tick = world.Tick;
        if (world.Player.IsKnockedOut(tick))
        {
            return "Ви без тями.";
        }
        if (world.Player.IsAsleep(tick))
        {
            return "Ви спите.";
        }
        if (npc.IsKnockedOut(tick))
        {
            return npc.Name + " без тями.";
        }
        if (npc.IsAsleep)
        {
            return npc.Name + " спить.";
        }
        if (npc.RefuseTalkUntilTick > tick)
        {
            return npc.Name + " не хоче з вами говорити (до " + world.TimeOf(npc.RefuseTalkUntilTick).Split(", ")[^1] + ").";
        }
        return null;
    }

    /// <summary>Сейчас нельзя ни писать, ни делать что-то в разговоре: ждём ответа или собеседник недоступен.</summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(false, nameof(Dialog))]
    private bool DialogBlocked => Dialog is null || Dialog.Pending is not null || CannotTalkReason(Dialog.Npc) is not null;

    /// <summary>Окно разговора открывается всегда: история видна, а писать нельзя, пока есть причина.</summary>
    public void OpenDialog(Npc npc)
    {
        if (Dialog is null)
        {
            _pausedBeforeDialog = Paused;
        }
        Dialog = new DialogState(npc);
        foreach (var line in _memory.For(npc.Id).History)
        {
            Dialog.Lines.Add(new DialogLine(line.Player ? Player.PlayerId : npc.Id, line.Text, line.Player));
        }
        Dialog.FocusInput = true;
        SelectedNpc = npc;
        Paused = true;
    }

    /// <summary>После разговора мир продолжает идти, если до него не стоял на паузе.</summary>
    public void CloseDialog()
    {
        if (Dialog is null)
        {
            return;
        }
        Dialog = null;
        Paused = _pausedBeforeDialog;
        SaveProfile();
    }

    public void SendDialogText(string text)
    {
        if (DialogBlocked || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Dialog.Lines.Add(new DialogLine(Player.PlayerId, text, true));
        Dialog.Input = string.Empty;
        DialogsSent++;
        if (PlayerStole)
        {
            DialogsAfterTheft++;
        }
        var quest = Simulation.World.Quests.QuestOfGiver(Dialog.Npc.Id);
        if (quest is not null && !quest.IsCompletedOrRewarded &&
            !Simulation.World.Player.HasItem(quest.RequiredItem))
        {
            TriedToDeceive = true;
        }
        Dialog.Pending = Simulation.AskNpcAsync(Dialog.Npc, text, Dialog.Lines.ToList());
    }

    private void UpdateDialog()
    {
        if (Dialog is null)
        {
            return;
        }

        if (Dialog.Pending is { IsCompleted: true } task)
        {
            DecisionResult? result = null;
            if (!task.IsFaulted && !task.IsCanceled)
            {
                result = task.Result;
            }
            var npc = Dialog.Npc;
            var say = result?.Executed.Say;
            if (string.IsNullOrWhiteSpace(say))
            {
                say = npc.LastSay ?? "…";
            }
            Dialog.Lines.Add(new DialogLine(npc.Id, say, false));
            var actionId = result?.Executed.Action;
            if (!string.IsNullOrEmpty(actionId) && actionId is not ("say" or "ignore"))
            {
                Dialog.LineActions[Dialog.Lines.Count - 1] = actionId;
            }
            Dialog.LastResult = result;
            Dialog.LastTrace = Simulation.Traces.LastOrDefault(t => string.Equals(t.NpcId, npc.Id, StringComparison.Ordinal));
            Dialog.Pending = null;
            Dialog.FocusInput = true;

            if (PlayerMode)
            {
                Remember(npc, result?.Proposed);
                if (string.Equals(actionId, "end_talk", StringComparison.Ordinal))
                {
                    ShowNotice(npc.Name + Svidok.Core.Util.UkGrammar.G(npc, " обірвав", " обірвала") + " розмову.");
                }
            }
        }
    }

    // ---------------------------------------------------------------- поступки игрока в разговоре

    /// <summary>Поступок: событие в мире, затем реплика с описанием, чтобы NPC отреагировал.</summary>
    private void SendDeed(string deed, string? words)
    {
        if (Dialog is null)
        {
            return;
        }
        var text = "(" + deed + ")";
        if (!string.IsNullOrWhiteSpace(words))
        {
            text += " " + words.Trim();
        }
        SendDialogText(text);
    }

    public void DeedGiveCoins(int coins, string purpose, string? words)
    {
        if (DialogBlocked)
        {
            return;
        }
        var npc = Dialog.Npc;
        if (!GameMechanics.PlayerGiveCoins(Simulation.World, npc, coins))
        {
            ShowNotice("Не вистачає монет.");
            return;
        }
        SendDeed("дає " + coins + " " + Coins(coins) + purpose, words);
    }

    public void DeedGiveItem(string itemId, string? words)
    {
        if (DialogBlocked)
        {
            return;
        }
        var npc = Dialog.Npc;
        var name = Simulation.World.ItemName(itemId);
        var quest = Simulation.PlayerGiveItem(itemId, npc);
        if (quest is not null && quest.State == Svidok.Core.Quests.QuestState.Completed)
        {
            ShowNotice("Завдання виконано: " + quest.TitleUk);
        }
        SendDeed("дарує вам: " + name, words);
    }

    public void DeedOfferTrade(string playerItem, string? npcItem, string? words)
    {
        if (DialogBlocked)
        {
            return;
        }
        var world = Simulation.World;
        if (!GameMechanics.PlayerOfferTrade(world, Dialog.Npc, playerItem, npcItem))
        {
            return;
        }
        var deed = npcItem is null
            ? "пропонує обміняти " + world.ItemName(playerItem) + " на щось ваше"
            : "пропонує обміняти " + world.ItemName(playerItem) + " на ваш " + world.ItemName(npcItem);
        SendDeed(deed, words);
    }

    public void DeedThreaten(string? words)
    {
        if (DialogBlocked)
        {
            return;
        }
        GameMechanics.PlayerThreaten(Simulation.World, Dialog.Npc, words);
        SendDeed("погрожує вам", words);
    }

    public void DeedHit(string? words)
    {
        if (DialogBlocked)
        {
            return;
        }
        var npc = Dialog.Npc;
        GameMechanics.PlayerHit(Simulation.World, npc);
        if (npc.IsKnockedOut(Simulation.World.Tick))
        {
            ShowNotice(npc.Name + " без тями.");
            return;
        }
        SendDeed("б'є вас", words);
    }

    public void DeedGesture(string gesture, string? words)
    {
        if (DialogBlocked)
        {
            return;
        }
        SendDeed(gesture, words);
    }

    public void DeedPayDemand()
    {
        if (DialogBlocked)
        {
            return;
        }
        var world = Simulation.World;
        if (!world.Game.Demands.TryGetValue(Dialog.Npc.Id, out var demand))
        {
            return;
        }
        if (!GameMechanics.PlayerPayDemand(world, Dialog.Npc))
        {
            ShowNotice("Не вистачає монет.");
            return;
        }
        SendDeed("платить " + demand.Coins + " " + Coins(demand.Coins), null);
    }

    public void DeedRefuseDemand()
    {
        if (DialogBlocked)
        {
            return;
        }
        GameMechanics.PlayerRefuseDemand(Simulation.World, Dialog.Npc);
        SendDeed("відмовляється платити", null);
    }

    private static string Coins(int n) => (n % 10) switch
    {
        1 when n % 100 != 11 => "монету",
        2 or 3 or 4 when n % 100 is < 12 or > 14 => "монети",
        _ => "монет"
    };

    /// <summary>
    /// После ответа NPC: история, доверие, новая заметка об игроке и, если модель сочла это важным,
    /// событие-разговор, которое другие могут подслушать и пересказать как слух.
    /// </summary>
    private void Remember(Npc npc, LlmDecision? decision)
    {
        var world = Simulation.World;
        var memory = _memory.For(npc.Id);
        memory.Talks++;
        world.Game.Acquainted.Add(npc.Id);
        memory.LastTalkTick = world.Clock.Tick;
        if (Dialog is not null)
        {
            memory.History = Dialog.Lines
                .TakeLast(HistoryLines)
                .Select(l => new SavedLine { Player = l.IsPlayer, Text = l.Text })
                .ToList();
        }

        if (Dialog is not null)
        {
            Dialog.LastAttitude = 0;
        }
        if (decision?.AttitudeDelta is int delta && delta != 0)
        {
            world.Trust.Adjust(npc.Id, Player.PlayerId, delta * 0.06);
            if (Dialog is not null)
            {
                Dialog.LastAttitude = delta;
            }
        }
        memory.Trust = world.Trust.Get(npc.Id, Player.PlayerId);

        var note = decision?.MemoryNote;
        if (!string.IsNullOrWhiteSpace(note))
        {
            AppendNote(memory, "[" + world.Clock.TimeString + "] " + note.Trim());
            if (decision!.Gossip)
            {
                var zone = world.ZoneOfTile(npc.X, npc.Y);
                var talk = world.Log.Add(world.Clock.Tick, "talk", npc.Id, Player.PlayerId, null, zone, npc.X, npc.Y,
                    0.6, 0.4, false,
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["text"] = npc.Name + " про " + PlayerName + ": " + note.Trim()
                    });
                npc.Knowledge.AddFirstHand(talk, Source.Participant, 1.0, false, world.Clock.Tick, npc.Id);
            }
            if (memory.Notes.Length > MemoryCompactAt)
            {
                StartCompaction(npc, memory);
            }
        }

        SaveProfile();
    }

    private static void AppendNote(NpcMemory memory, string line)
    {
        var lines = memory.Notes
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        lines.Add(line);
        while (lines.Count > 1 && lines.Sum(l => l.Length + 1) > MemoryHardCap)
        {
            lines.RemoveAt(0);
        }
        memory.Notes = string.Join("\n", lines);
    }

    /// <summary>Фоновое сжатие заметок: разговор при этом не останавливается.</summary>
    private void StartCompaction(Npc npc, NpcMemory memory)
    {
        if (_compactions.ContainsKey(npc.Id) || Simulation.LlmMaker is not LlmDecisionMaker maker)
        {
            return;
        }

        const string system = "Ти стискаєш нотатки персонажа гри про гравця. Відповідай лише json виду {\"memory\": \"...\"}.";
        var user = "Нотатки персонажа " + npc.Name + " про гравця " + PlayerName + ":\n" + memory.Notes + "\n\n" +
                   "Перепиши їх коротше, до 1500 символів. Збережи імена, обіцянки, борги, образи, домовленості, " +
                   "ставлення і важливі факти; прибери повтори й дрібниці. Кожен факт з нового рядка, " +
                   "на початку рядка залиш позначку часу першої згадки у квадратних дужках. Поверни json.";
        _compactions[npc.Id] = maker.CompleteTextAsync(system, user, 1500, CancellationToken.None);
    }

    private void UpdateCompactions()
    {
        if (_compactions.Count == 0)
        {
            return;
        }

        foreach (var npcId in _compactions.Keys.ToList())
        {
            var task = _compactions[npcId];
            if (!task.IsCompleted)
            {
                continue;
            }
            _compactions.Remove(npcId);
            if (task.IsFaulted || task.IsCanceled || string.IsNullOrWhiteSpace(task.Result))
            {
                continue;
            }

            var compact = ReadMemoryJson(task.Result!);
            var memory = _memory.For(npcId);
            if (string.IsNullOrWhiteSpace(compact) || compact.Length >= memory.Notes.Length)
            {
                continue;
            }
            memory.Notes = compact.Length > MemoryHardCap ? compact[..MemoryHardCap] : compact.Trim();
            SaveProfile();
        }
    }

    private static string? ReadMemoryJson(string content)
    {
        try
        {
            using var doc = JsonDocument.Parse(ResponseParser.ExtractJson(content));
            return doc.RootElement.TryGetProperty("memory", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Без разговора больше суток игрового времени отношение медленно остывает к обычному.</summary>
    private const int DriftAfterTicks = 24 * 60;

    private const double DriftPerHour = 0.01;

    /// <summary>
    /// Раз в игровой час: остывание отношения у тех, с кем давно не говорили, и сохранение профиля,
    /// чтобы доверие не терялось при закрытии окна или сбое.
    /// </summary>
    private void DriftTrust()
    {
        if (!PlayerMode)
        {
            return;
        }
        var world = Simulation.World;
        var hour = world.Clock.Tick / 60;
        if (hour == _lastHour)
        {
            return;
        }
        _lastHour = hour;

        var baseline = world.Game.Social
            ? Math.Max(world.Config.TrustDefaults.Player, GameRules.NewcomerTrust)
            : world.Config.TrustDefaults.Player;
        foreach (var (npcId, memory) in _memory.Npcs)
        {
            if (memory.LastTalkTick >= 0 && world.Clock.Tick - memory.LastTalkTick < DriftAfterTicks)
            {
                continue;
            }
            var current = world.Trust.Get(npcId, Player.PlayerId);
            var next = current + (baseline - current) * DriftPerHour;
            world.Trust.Set(npcId, Player.PlayerId, next);
            if (memory.Trust >= 0)
            {
                memory.Trust = world.Trust.Get(npcId, Player.PlayerId);
            }
        }
        SaveProfile();
    }

    /// <summary>Данные для промпта живого разговора (раздел игры, не экспериментов).</summary>
    private InteractiveDialog? BuildInteractive(Npc npc)
    {
        var world = Simulation.World;
        var memory = _memory.For(npc.Id);
        var gossip = npc.Knowledge.Entries
            .Where(e => string.Equals(e.Type, "talk", StringComparison.Ordinal))
            .Where(e => string.Equals(e.Field("target"), Player.PlayerId, StringComparison.Ordinal) ||
                        string.Equals(e.Field("actor"), Player.PlayerId, StringComparison.Ordinal))
            .Where(e => !string.Equals(e.OriginId, npc.Id, StringComparison.Ordinal))
            .Where(e => !string.IsNullOrWhiteSpace(e.Text))
            .OrderByDescending(e => e.C)
            .ThenByDescending(e => e.UpdatedTick)
            .Take(5)
            .Select(e => e.Text + " (почув від: " + world.NpcName(e.Chain.LastOrDefault() ?? e.OriginId) +
                         ", достовірність " + e.C.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + ")")
            .ToList();

        _styles.TryGetValue(npc.Id, out var style);

        var wares = npc.Inventory
            .Distinct(StringComparer.Ordinal)
            .Select(i => i + " (" + world.ItemName(i) +
                         (world.Items.TryGetValue(i, out var def) && def.Price > 0 ? ", ~" + def.Price + " мон." : string.Empty) + ")")
            .ToList();

        var deals = new List<string>();
        if (world.Game.Trades.TryGetValue(npc.Id, out var trade))
        {
            deals.Add("Гравець пропонує обмін: свій " + trade.PlayerItem + " (" + world.ItemName(trade.PlayerItem) + ")" +
                      (trade.NpcItem is null ? " на щось твоє" : " на твій " + trade.NpcItem) +
                      ". Погодитись — accept_trade з item, відмовитись — просто скажи.");
        }
        if (world.Game.Demands.TryGetValue(npc.Id, out var demand))
        {
            deals.Add("Ти вже вимагав від гравця " + demand.Coins + " монет" +
                      (demand.Item is null ? string.Empty : " за " + world.ItemName(demand.Item)) + ", він ще не відповів.");
        }
        var paid = world.Game.PaidRecently(npc.Id, world.Tick, GameRules.PaymentWindowTicks);
        if (paid > 0)
        {
            deals.Add("Гравець щойно дав тобі " + paid + " монет.");
        }
        var player = world.Player;
        if (player.IsBanished(world.Tick))
        {
            deals.Add("Гравця вигнали з селища.");
        }

        return new InteractiveDialog(PlayerName, memory.Notes, world.Trust.Get(npc.Id, Player.PlayerId), style, gossip,
            wares, deals.Count == 0 ? null : string.Join("\n", deals));
    }

    private static Dictionary<string, string> LoadStyles()
    {
        try
        {
            var path = PathResolver.ResolveExisting("data/npc_styles.json");
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.Ordinal);
            }
            return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path, Encoding.UTF8))
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Warn("Манери мовлення не прочитано: " + ex.Message);
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }

    /// <summary>Что случилось с игроком: арест, изгнание, штраф, удар, подарок, находка подброшенного.</summary>
    private void NoticeForPlayer(WorldEvent ev)
    {
        var world = Simulation.World;
        var toPlayer = string.Equals(ev.TargetId, Player.PlayerId, StringComparison.Ordinal);
        var byPlayer = string.Equals(ev.ActorId, Player.PlayerId, StringComparison.Ordinal);
        var who = world.NpcName(ev.ActorId);
        string? text = ev.Type switch
        {
            "arrest" when toPlayer => who + " кинув вас в обезьянник до " + world.TimeOf(world.Player.JailedUntilTick).Split(", ")[^1] + ".",
            "banish" when toPlayer => who + " вигнав вас із селища до " + world.TimeOf(world.Player.BanishedUntilTick).Split(", ")[^1] + ". Не повертайтесь раніше: варта посадить.",
            "fine" when toPlayer => who + " оштрафував вас.",
            "curfew" when byPlayer => "Вартовий оштрафував вас за прогулянку вночі. Ночуйте вдома.",
            "attack" when toPlayer => who + " вдарив вас. Здоров'я: " + world.Player.Hp + "/" + world.Player.MaxHp + ".",
            "knockout" when toPlayer => "Ви знепритомніли. Прийдете до тями за годину.",
            "give_item" when toPlayer => who + " дав вам: " + world.ItemName(ev.ItemId) + ".",
            "quest_given" when toPlayer => who + " доручив вам завдання" +
                                           (ev.ItemId is null ? "." : " і дав «" + world.ItemName(ev.ItemId) + "».") + " Деталі — O.",
            "reward_given" when toPlayer => who + " заплатив вам" + (ev.Get("coins") is { } paid ? " " + paid + " мон." : ".") +
                                            " Тепер вам більше довіряють.",
            "trade" when byPlayer => "Обмін відбувся.",
            "item_found" => world.NpcName(ev.ActorId) + " знайшов " + world.ItemName(ev.ItemId) + " у: " + world.ZoneName(ev.ZoneId) + ".",
            "work" when byPlayer => "Робота зроблена, заплатили монети.",
            "search" when toPlayer => ev.Get("result") == "found"
                ? who + " обшукав вас і забрав «" + world.ItemName(ev.ItemId) + "». Зараз прийде варта."
                : who + " обшукав вас і нічого не знайшов.",
            "search" => who + " обшукав " + world.NpcName(ev.TargetId) +
                        (ev.Get("result") == "found" ? " і знайшов «" + world.ItemName(ev.ItemId) + "»." : ": нічого."),
            "recovered" => who + " знайшов пропажу: «" + world.ItemName(ev.ItemId) + "».",
            _ => null
        };
        if (text is not null)
        {
            ShowNotice(byPlayer ? text : Svidok.Core.Util.UkGrammar.For(world.Npc(ev.ActorId), text));
        }
    }

    public void SaveProfile()
    {
        if (_profile is null)
        {
            return;
        }
        SyncTrust();
        PlayerProfiles.Save(_profile);
    }

    /// <summary>
    /// Доверие меняется не только в разговоре: подарки, работа, увиденная кража, подтверждённые
    /// или опровергнутые слова. Перед сохранением переносим в профиль отношение всех жителей,
    /// у кого оно отличается от обычного, а не только тех, с кем говорили.
    /// </summary>
    private void SyncTrust()
    {
        var world = Simulation.World;
        var baseline = world.Game.Social
            ? Math.Max(world.Config.TrustDefaults.Player, GameRules.NewcomerTrust)
            : world.Config.TrustDefaults.Player;
        foreach (var npc in world.Npcs)
        {
            var value = world.Trust.Get(npc.Id, Player.PlayerId);
            if (_memory.Npcs.TryGetValue(npc.Id, out var memory))
            {
                memory.Trust = value;
            }
            else if (Math.Abs(value - baseline) > 0.005)
            {
                _memory.For(npc.Id).Trust = value;
            }
        }
    }

    public void EmitEvent(string type, string? actorId, string? targetId, string? itemId, string? zoneId, int x, int y,
        Dictionary<string, string>? extra = null)
    {
        Simulation.EmitManualEvent(type, actorId, targetId, itemId, zoneId, x, y, extra);
        ShowNotice("Подію створено: " + Simulation.World.Schema.Get(type).NameUk);
    }

    public void ExportCsv()
    {
        var outcome = new BatchOutcome();
        outcome.Runs.Add(Simulation.Metrics.ToRunRow(Simulation));
        outcome.S1Coverage.AddRange(Simulation.Metrics.S1Coverage);
        var summary = Simulation.Metrics.S1Summary;
        summary.Variant = _variant.ToString();
        summary.Seed = _seed;
        outcome.S1Summary.Add(summary);
        outcome.S4Hourly.AddRange(Simulation.Metrics.S4Hourly);
        outcome.LlmCalls.AddRange(Simulation.Metrics.LlmCalls);

        var dir = Path.Combine("results", "manual_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        outcome.Write(PathResolver.ResolveExisting(dir));
        ShowNotice("CSV збережено: " + dir);
    }

    public void Dispose()
    {
        SaveProfile();
        Simulation.Dispose();
    }
}

/// <summary>Состояние окна диалога игрока.</summary>
public sealed class DialogState
{
    public DialogState(Npc npc)
    {
        Npc = npc;
        Input = string.Empty;
    }

    public Npc Npc { get; }

    public List<DialogLine> Lines { get; } = new();

    public string Input;

    public Task<DecisionResult>? Pending { get; set; }

    public DecisionResult? LastResult { get; set; }

    public DecisionTrace? LastTrace { get; set; }

    /// <summary>Действие NPC, выполненное вместе с репликой (номер строки — id действия).</summary>
    public Dictionary<int, string> LineActions { get; } = new();

    /// <summary>Поставить курсор в поле ввода на следующем кадре.</summary>
    public bool FocusInput { get; set; }

    /// <summary>Прокрутить историю к последней реплике.</summary>
    public bool ScrollToEnd { get; set; } = true;

    /// <summary>Как изменилось отношение после последней реплики (-2..2).</summary>
    public int LastAttitude { get; set; }
}
