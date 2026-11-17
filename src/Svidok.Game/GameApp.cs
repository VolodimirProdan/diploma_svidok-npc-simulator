using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;
using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Scenarios;
using Svidok.Core.Util;
using Svidok.Llm;
using Color = Raylib_cs.Color;

namespace Svidok.Game;

public enum AppScreen
{
    Menu,
    Simulation
}

/// <summary>Главное окно Raylib + ImGui: меню, режимы, панели, диалоги.</summary>
public sealed class GameApp : IDisposable
{
    private static IntPtr _glyphRangesPtr = IntPtr.Zero;
    private readonly GameOptions _options;
    private Font _font;
    private bool _customFont;
    private MenuScreen _menu = null!;
    private WorldRenderer _renderer = null!;
    private ObserverPanel _panel = null!;
    private PlayerController _playerController = null!;
    private SimulationHost? _host;
    private bool _quit;
    private bool _showPanel;
    private bool _pauseMenu;
    private bool _helpOpen;
    private bool _goalsOpen = true;

    private bool _gossipOpen;

    private bool _questsOpen;

    private bool _storyOpen;
    private bool _hideUi;
    private bool _eventModal;
    private bool _traceModal;
    private int _traceIndex;
    private bool _shoutModal;
    private string _shoutText = string.Empty;
    private string _eventActor = string.Empty;
    private string _eventTarget = string.Empty;
    private string _eventItem = string.Empty;
    private string _eventZone = string.Empty;
    private int _eventTypeIndex;
    private int _eventTileX;
    private int _eventTileY;
    private string _notice = string.Empty;
    private double _noticeUntil;

    public GameApp(GameOptions? options = null)
    {
        _options = options ?? new GameOptions();
        Config = AppConfig.LoadDefault();
        ApiKeyStore.ApplyToEnvironment(Config.Llm.ApiKeyEnv);
        Catalog = ActionCatalog.LoadDefault();
        Tokens = new TokenCounter();
    }

    public AppConfig Config { get; }

    public ActionCatalog Catalog { get; }

    public TokenCounter Tokens { get; }

    public AppScreen Screen { get; private set; } = AppScreen.Menu;

    public SimulationHost? Host => _host;

    public bool ShowPanel => _showPanel;

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.VSyncHint);
        Raylib.InitWindow(Config.Ui.Width, Config.Ui.Height, UiText.AppTitle);
        Raylib.SetWindowMinSize(1280, 720);
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null);

        PrepareUiFont();
        rlImGui.Setup(true, false);
        ApplyStyle();
        _font = LoadMapFont();
        _renderer = new WorldRenderer(_font);
        _panel = new ObserverPanel();
        _menu = new MenuScreen();
        if (_options.AutoStart)
        {
            AutoStart();
        }

        var frames = 0;
        while (!Raylib.WindowShouldClose() && !_quit)
        {
            frames++;
            var dt = Raylib.GetFrameTime();
            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(26, 28, 32, 255));

            rlImGui.Begin(dt);

            if (Screen == AppScreen.Menu)
            {
                DrawMenuBackdrop();
                var result = _menu.Draw(this);
                if (result is not null)
                {
                    StartSimulation(result);
                }
            }
            else if (_host is not null)
            {
                DrawSimulation(dt);
            }

            rlImGui.End();
            Raylib.EndDrawing();

            if (_options.MaxFrames > 0 && frames >= _options.MaxFrames)
            {
                if (!string.IsNullOrWhiteSpace(_options.ScreenshotPath))
                {
                    SaveScreenshot(_options.ScreenshotPath!);
                }
                break;
            }
        }

        _host?.Dispose();
        _host = null;
        if (_customFont)
        {
            Raylib.UnloadFont(_font);
        }
        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }

    private void DrawMenuBackdrop()
    {
        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        for (var i = 0; i < 12; i++)
        {
            var color = new Color(30 + i * 2, 34 + i * 2, 40 + i * 2, 255);
            Raylib.DrawRectangle(0, height * i / 12, width, height / 12 + 1, color);
        }
    }

    private void DrawSimulation(float dt)
    {
        var host = _host!;
        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        var panelWidth = _showPanel && !_hideUi ? 480 : 0;
        var viewport = _hideUi
            ? new Rectangle(0, 0, width, height)
            : new Rectangle(0, 36, Math.Max(320, width - panelWidth), Math.Max(200, height - 60));

        host.Update(dt);
        if (_options.Heatmap)
        {
            host.ShowHeatmap = true;
            host.LockEventSelection = true;
            if (host.Simulation.Metrics.S1TargetEventId is int heatTarget)
            {
                host.SelectedEventId = heatTarget;
            }
        }
        host.UpdateCamera(viewport);

        var io = ImGui.GetIO();
        if (host.PlayerMode && !AnyOverlay(host) && host.Dialog is null && !io.WantCaptureKeyboard)
        {
            _playerController.Update(dt);
        }

        _renderer.Draw(host, viewport);
        if (!_hideUi)
        {
            DrawTopBar(host, width);
            DrawBottomBar(host, width, height);

            if (_showPanel)
            {
                _panel.Draw(this, host);
            }

            ObjectivesPanel.Draw(host, ref _goalsOpen);
            if (host.PlayerMode)
            {
                GossipPanel.Draw(host, ref _gossipOpen);
                if (host.Simulation.World.Game.Social)
                {
                    QuestBoard.Draw(host, ref _questsOpen);
                    StoryPanel.Draw(host, ref _storyOpen);
                }
            }
        }

        if (host.PlayerMode)
        {
            DialogWindow.Draw(this, host);
        }

        HandleKeys(host);
        if (_host is null)
        {
            return;
        }
        DrawTalkTooltip(host);
        DrawModals(host);
        if (_host is null)
        {
            return;
        }
        DrawNotice(host);

        // в режиме снимка тепловая карта всегда показывает событие S1, даже если мышь кликнула по таблице
        if (_options.Heatmap && host.Simulation.Metrics.S1TargetEventId is int pinnedEvent)
        {
            host.SelectedEventId = pinnedEvent;
        }
    }

    private static readonly Vector4 PauseColor = new(0.95f, 0.45f, 0.40f, 1f);
    private static readonly Vector4 AiOnColor = new(0.45f, 0.80f, 0.95f, 1f);
    private static readonly Vector4 AiOffColor = new(0.75f, 0.75f, 0.78f, 1f);
    private static readonly Vector4 ActiveButton = new(0.26f, 0.45f, 0.70f, 1f);

    private void DrawTopBar(SimulationHost host, int width)
    {
        ImGui.SetNextWindowPos(new Vector2(0, 0), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(width, 36), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 6));
        if (ImGui.Begin("##topbar", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                       ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                                       ImGuiWindowFlags.NoBringToFrontOnFocus))
        {
            var variant = host.Simulation.Variant;
            ImGui.TextUnformatted(host.Scenario.Id.ToUpperInvariant() + " · " + host.Scenario.NameUk);
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextUnformatted(host.Simulation.World.Clock.TimeString);
            ImGui.SameLine();
            ImGui.TextDisabled("|");
            ImGui.SameLine();
            ImGui.TextColored(variant == Variant.V0 ? AiOffColor : AiOnColor,
                variant == Variant.V0 ? "Без ШІ: V0" : "ШІ: " + variant + " · " + Variants.NameUk(variant).ToLowerInvariant());
            ImGui.SameLine();

            for (var i = 0; i < 5; i++)
            {
                if (i > 0)
                {
                    ImGui.SameLine();
                }
                var active = host.Paused ? i == 0 : host.SpeedIndex == i;
                if (active)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, ActiveButton);
                }
                if (ImGui.Button(UiText.SpeedLabel(i) + "##speed" + i, new Vector2(i == 0 ? 56 : 44, 24)))
                {
                    host.SpeedIndex = i;
                    host.Paused = i == 0;
                }
                if (active)
                {
                    ImGui.PopStyleColor();
                }
            }

            ImGui.SameLine();
            if (host.Simulation.Finished)
            {
                ImGui.TextColored(PauseColor, UiText.ScenarioOverTitle.ToUpperInvariant());
            }
            else if (host.Paused)
            {
                ImGui.TextColored(PauseColor, host.Dialog is not null ? "ПАУЗА · розмова" : "ПАУЗА");
            }
            else if (host.Simulation.ContinueAfterEnd)
            {
                ImGui.TextDisabled("після сценарію");
            }

            if (host.PlayerMode)
            {
                var player = host.Simulation.World.Player;
                ImGui.SameLine();
                ImGui.TextDisabled("|");
                ImGui.SameLine();
                ImGui.TextUnformatted("Предметів: " + player.Inventory.Count + " · " + player.Coins + " монет");
                var game = host.Simulation.World.Game;
                var tick = host.Simulation.World.Tick;
                if (game.Combat || game.Life)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(player.Hp <= 3 ? PauseColor : new Vector4(0.45f, 0.85f, 0.50f, 1f),
                        "Здоров'я " + player.Hp + "/" + player.MaxHp);
                }
                if (game.Life)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(player.Hunger >= 80 ? PauseColor : player.Hunger >= 50 ? new Vector4(0.95f, 0.80f, 0.35f, 1f) : AiOffColor,
                        "Голод " + (int)player.Hunger + "%");
                }
                var status = player.IsKnockedOut(tick) ? "без тями"
                    : player.IsAsleep(tick) ? "спить"
                    : player.IsJailed(tick) ? "в обезьяннику"
                    : player.IsBanished(tick) ? "вигнаний"
                    : player.WorkUntilTick > tick ? "працює"
                    : null;
                if (status is not null)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(PauseColor, status);
                }
            }

            if (host.PlayerMode)
            {
                var asleep = host.Simulation.World.Player.IsAsleep(host.Simulation.World.Tick);
                ImGui.SameLine(width - 232 - 196 - 84 - 104 - 94);
                var hasStory = host.Simulation.World.Game.Stories.Count > 0;
                ImGui.BeginDisabled(!hasStory);
                if (ImGui.Button("Сюжет (K)", new Vector2(90, 24)))
                {
                    _storyOpen = !_storyOpen;
                }
                ImGui.EndDisabled();
                ImGui.SetItemTooltip(hasStory ? "Що відбувається в селі і що потрібно, щоб піти далі." : "У цьому сценарії немає сюжету.");
                ImGui.SameLine();
                ImGui.BeginDisabled(!host.Simulation.World.Game.Social);
                if (ImGui.Button("Завдання (O)", new Vector2(100, 24)))
                {
                    _questsOpen = !_questsOpen;
                }
                ImGui.EndDisabled();
                ImGui.SetItemTooltip("Дошка оголошень: хто просить допомогти, ваші завдання, де заробити.");
                ImGui.SameLine();
                if (ImGui.Button("Плітки (P)", new Vector2(80, 24)))
                {
                    _gossipOpen = !_gossipOpen;
                }
                ImGui.SetItemTooltip("Що про вас знають і говорять у селі.");
                ImGui.SameLine();
                ImGui.BeginDisabled(asleep);
                if (ImGui.Button("Спати (B)", new Vector2(92, 24)))
                {
                    host.Sleep();
                }
                ImGui.EndDisabled();
                ImGui.SetItemTooltip("Ночью — до ранку, вдень — дві години. Якщо є свій дім, спати можна лише там.");
                ImGui.SameLine();
                if (ImGui.Button("Зберегти", new Vector2(92, 24)))
                {
                    host.SaveGame();
                }
                ImGui.SetItemTooltip(host.CurrentSave is { } save
                    ? "Останнє збереження: " + save.Saved.ToString("dd.MM HH:mm") + ". Наступного разу гра продовжиться з цього місця."
                    : "Запам'ятати місце, монети, речі, здоров'я та ставлення жителів. Наступного разу гра продовжиться звідси.");
            }

            ImGui.SameLine(width - 232);
            if (ImGui.Button(UiText.GoalsButton, new Vector2(84, 24)))
            {
                _goalsOpen = !_goalsOpen;
            }
            ImGui.SameLine();
            if (ImGui.Button("?", new Vector2(28, 24)))
            {
                _helpOpen = !_helpOpen;
            }
            ImGui.SameLine();
            if (ImGui.Button("Меню", new Vector2(60, 24)))
            {
                _pauseMenu = true;
                host.Paused = true;
            }
            ImGui.SameLine();
            if (ImGui.Button(host.Paused ? ">" : "||", new Vector2(28, 24)))
            {
                host.Paused = !host.Paused;
                if (!host.Paused && host.SpeedIndex == 0)
                {
                    host.SpeedIndex = 1;
                }
            }
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawBottomBar(SimulationHost host, int width, int height)
    {
        var text = host.LastEventText;
        var staleByGameTime = host.LastEventTick >= 0 && host.Simulation.World.Clock.Tick - host.LastEventTick > 120;
        if (text is null || Raylib.GetTime() - host.LastEventAt > 5.0 || staleByGameTime)
        {
            text = host.PlayerMode
                ? "WASD — рух · клік/E — говорити · Q — вкрасти · F — підняти · G — віддати · I — інвентар" +
                  (host.Simulation.World.Game.Combat ? " · X — вдарити" : string.Empty) +
                  (host.Simulation.World.Game.Life ? " · Z — їсти" : string.Empty) +
                  (host.Simulation.World.Game.Life || host.Simulation.World.Game.Social ? " · J — працювати · O — завдання" : string.Empty) +
                  " · B — спати · P — плітки" +
                  " · F1 — довідка"
                : "ЛКМ — вибрати персонажа · ПКМ — камера · колесо — масштаб · Пробіл — пауза · 1–5 — швидкість · R — теплова карта · F1 — довідка";
        }
        else
        {
            text = "Подія: " + text;
        }

        ImGui.SetNextWindowPos(new Vector2(0, height - 24), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(width, 24), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 2));
        if (ImGui.Begin("##bottom", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                      ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                                      ImGuiWindowFlags.NoBringToFrontOnFocus))
        {
            ImGui.TextUnformatted(text);
            if (Variants.UsesLlm(host.Simulation.Variant))
            {
                var stats = "Запитів до ШІ: " + host.Simulation.Metrics.LlmCount + " · $" +
                            host.Simulation.Metrics.CostUsd.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture);
                ImGui.SameLine(Math.Max(0, width - ImGui.CalcTextSize(stats).X - 16));
                ImGui.TextDisabled(stats);
            }
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    private void DrawNotice(SimulationHost host)
    {
        // сообщения приходят из двух мест: окно (клавиши, меню) и сессия (мир, разговоры); показываем свежее
        var text = _notice;
        var until = _noticeUntil;
        if (!string.IsNullOrEmpty(host.Notice) && host.NoticeUntil > until)
        {
            text = host.Notice;
            until = host.NoticeUntil;
        }
        if (string.IsNullOrEmpty(text) || Raylib.GetTime() > until)
        {
            return;
        }
        var width = Raylib.GetScreenWidth();
        ImGui.SetNextWindowPos(new Vector2(width / 2f - 220, 60), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(440, 0), ImGuiCond.Always);
        // уведомление не должно забирать фокус: иначе в поле ввода разговора пропадает курсор
        if (ImGui.Begin("##notice", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                     ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar |
                                     ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing |
                                     ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoNav |
                                     ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoSavedSettings))
        {
            ImGui.TextWrapped(text);
        }
        ImGui.End();
    }

    public void ShowNotice(string text)
    {
        _notice = text;
        _noticeUntil = Raylib.GetTime() + 4.0;
    }

    /// <summary>Окно «сценарій завершено» ждёт ответа (в режиме снимков не показывается).</summary>
    private bool EndPending(SimulationHost host) =>
        _options.MaxFrames == 0 && host.FatalError is null && host.Simulation.Finished && !host.EndAcknowledged;

    private bool AnyOverlay(SimulationHost host) =>
        host.FatalError is not null || EndPending(host) || _pauseMenu || _traceModal || _eventModal ||
        _shoutModal || _helpOpen;

    /// <summary>Esc закрывает верхнее окно; если закрывать нечего — открывает паузу.</summary>
    private bool CloseTopOverlay(SimulationHost host)
    {
        if (host.FatalError is not null)
        {
            return true;
        }
        if (EndPending(host))
        {
            host.EndAcknowledged = true;
            return true;
        }
        if (_pauseMenu)
        {
            _pauseMenu = false;
            host.Paused = false;
            return true;
        }
        if (_traceModal)
        {
            _traceModal = false;
            return true;
        }
        if (_eventModal)
        {
            _eventModal = false;
            host.Paused = false;
            return true;
        }
        if (_shoutModal)
        {
            _shoutModal = false;
            host.Paused = false;
            return true;
        }
        if (_helpOpen)
        {
            _helpOpen = false;
            return true;
        }
        if (host.Dialog is not null)
        {
            host.CloseDialog();
            return true;
        }
        if (host.ShowInventory)
        {
            host.ShowInventory = false;
            return true;
        }
        return false;
    }

    private void HandleKeys(SimulationHost host)
    {
        var io = ImGui.GetIO();

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (!CloseTopOverlay(host))
            {
                _pauseMenu = true;
                host.Paused = true;
            }
            return;
        }

        if (io.WantCaptureKeyboard || AnyOverlay(host))
        {
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.F1))
        {
            _helpOpen = true;
            return;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.P) && host.PlayerMode)
        {
            _gossipOpen = !_gossipOpen;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.O) && host.PlayerMode && host.Simulation.World.Game.Social)
        {
            _questsOpen = !_questsOpen;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.K) && host.PlayerMode && host.Simulation.World.Game.Stories.Count > 0)
        {
            _storyOpen = !_storyOpen;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.F2))
        {
            _goalsOpen = !_goalsOpen;
            _hideUi = false;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.F3))
        {
            _hideUi = !_hideUi;
            ShowNotice(_hideUi ? "Інтерфейс сховано. F3 — показати." : "Інтерфейс показано");
        }

        if (host.Dialog is not null)
        {
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            host.Paused = !host.Paused;
            host.SpeedIndex = host.Paused ? 0 : Math.Max(1, host.SpeedIndex);
        }

        if (Raylib.IsKeyPressed(KeyboardKey.One))
        {
            host.SpeedIndex = 0;
            host.Paused = true;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Two))
        {
            host.SpeedIndex = 1;
            host.Paused = false;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Three))
        {
            host.SpeedIndex = 2;
            host.Paused = false;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Four))
        {
            host.SpeedIndex = 3;
            host.Paused = false;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Five))
        {
            host.SpeedIndex = 4;
            host.Paused = false;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Period) && host.Paused && !host.Finished)
        {
            host.Simulation.TickAsync(CancellationToken.None, waitForDecisions: false).GetAwaiter().GetResult();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.V))
        {
            host.ShowVision = !host.ShowVision;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.H))
        {
            host.ShowHearing = !host.ShowHearing;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.G) && !host.PlayerMode)
        {
            host.ShowGrid = !host.ShowGrid;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.L))
        {
            host.ShowNames = !host.ShowNames;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.T) && !host.PlayerMode)
        {
            host.ShowTrust = !host.ShowTrust;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.R))
        {
            host.ShowHeatmap = !host.ShowHeatmap;
            if (host.ShowHeatmap && host.SelectedEventId is null)
            {
                host.SelectedEventId = host.PlayerTheftEventId ?? host.LastImportantEventId;
            }
            if (host.ShowHeatmap && host.SelectedEventId is null)
            {
                ShowNotice("Поки немає події для теплової карти. Оберіть подію в журналі або дочекайтесь її.");
            }
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Tab) && host.ResearcherMode)
        {
            _showPanel = !_showPanel;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.I) && host.PlayerMode)
        {
            host.ShowInventory = !host.ShowInventory;
        }

        if (host.PlayerMode)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.E))
            {
                var npc = host.Simulation.World.NearestNpc(host.Simulation.World.Player.TileOf, 1.8);
                if (npc is not null)
                {
                    host.OpenDialog(npc);
                }
                else
                {
                    ShowNotice(UiText.NeedTarget);
                }
            }
            if (Raylib.IsKeyPressed(KeyboardKey.Y))
            {
                _shoutModal = true;
                host.Paused = true;
                _shoutText = string.Empty;
            }
        }

        if (!io.WantCaptureMouse && Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var mouse = Raylib.GetMousePosition();
            if (Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl))
            {
                var tile = host.TileAtScreen(mouse);
                if (host.Simulation.World.Map.InBounds(tile.X, tile.Y))
                {
                    _eventModal = true;
                    _eventTileX = tile.X;
                    _eventTileY = tile.Y;
                    _eventZone = host.Simulation.World.ZoneOfTile(tile.X, tile.Y);
                    host.Paused = true;
                }
            }
            else
            {
                var npc = host.NpcAtScreen(mouse);
                if (npc is not null)
                {
                    host.SelectedNpc = npc;
                    host.SelectedEventId = null;
                    if (host.PlayerMode)
                    {
                        if (CanTalk(host, npc))
                        {
                            host.OpenDialog(npc);
                        }
                        else
                        {
                            ShowNotice("Підійдіть ближче до " + npc.Name + ", щоб поговорити");
                        }
                    }
                }
            }
        }

        if (!io.WantCaptureMouse)
        {
            var mouse = Raylib.GetMousePosition();
            host.HoveredNpc = host.NpcAtScreen(mouse);
        }
    }

    private const double TalkDistance = 2.5;

    private static bool CanTalk(SimulationHost host, Npc npc)
    {
        var player = host.Simulation.World.Player;
        var dx = npc.X - player.X;
        var dy = npc.Y - player.Y;
        return Math.Sqrt(dx * dx + dy * dy) <= TalkDistance;
    }

    /// <summary>Подсказка над персонажем под мышью: кто это и можно ли с ним заговорить.</summary>
    private void DrawTalkTooltip(SimulationHost host)
    {
        if (!host.PlayerMode || host.Dialog is not null || AnyOverlay(host) || ImGui.GetIO().WantCaptureMouse)
        {
            return;
        }
        if (host.HoveredNpc is not { } npc)
        {
            return;
        }

        var memory = host.MemoryOf(npc);
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(npc.Name + " · " + Roles.NameUk(npc.Role));
        if (memory.Talks > 0)
        {
            ImGui.TextDisabled("Ви вже говорили: " + memory.Talks + " раз(и)");
        }
        if (host.CannotTalkReason(npc) is { } reason)
        {
            ImGui.TextColored(PauseColor, reason);
        }
        else
        {
            ImGui.TextColored(CanTalk(host, npc) ? new Vector4(0.45f, 0.85f, 0.50f, 1f) : new Vector4(0.75f, 0.75f, 0.78f, 1f),
                CanTalk(host, npc) ? "Клік — говорити" : "Підійдіть ближче, щоб поговорити");
        }
        ImGui.EndTooltip();
    }

    /// <summary>Общий вид интерфейса: скругления, отступы, спокойные тёмные цвета.</summary>
    private static void ApplyStyle()
    {
        var style = ImGui.GetStyle();
        style.WindowRounding = 8f;
        style.ChildRounding = 6f;
        style.FrameRounding = 5f;
        style.PopupRounding = 6f;
        style.GrabRounding = 5f;
        style.TabRounding = 5f;
        style.ScrollbarRounding = 6f;
        style.WindowPadding = new Vector2(12, 10);
        style.FramePadding = new Vector2(8, 5);
        style.ItemSpacing = new Vector2(8, 6);
        style.ItemInnerSpacing = new Vector2(6, 4);
        style.WindowBorderSize = 1f;
        style.FrameBorderSize = 0f;
        style.WindowTitleAlign = new Vector2(0.02f, 0.5f);

        var colors = style.Colors;
        colors[(int)ImGuiCol.WindowBg] = new Vector4(0.09f, 0.10f, 0.12f, 0.96f);
        colors[(int)ImGuiCol.ChildBg] = new Vector4(0.07f, 0.08f, 0.10f, 0.60f);
        colors[(int)ImGuiCol.PopupBg] = new Vector4(0.09f, 0.10f, 0.12f, 0.98f);
        colors[(int)ImGuiCol.Border] = new Vector4(0.25f, 0.27f, 0.32f, 0.70f);
        colors[(int)ImGuiCol.TitleBg] = new Vector4(0.13f, 0.15f, 0.19f, 1f);
        colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.17f, 0.22f, 0.30f, 1f);
        colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.13f, 0.15f, 0.19f, 0.90f);
        colors[(int)ImGuiCol.FrameBg] = new Vector4(0.16f, 0.18f, 0.22f, 1f);
        colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.21f, 0.24f, 0.30f, 1f);
        colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.24f, 0.28f, 0.36f, 1f);
        colors[(int)ImGuiCol.Button] = new Vector4(0.20f, 0.30f, 0.44f, 1f);
        colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.26f, 0.38f, 0.56f, 1f);
        colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.30f, 0.45f, 0.66f, 1f);
        colors[(int)ImGuiCol.Header] = new Vector4(0.20f, 0.30f, 0.44f, 0.70f);
        colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.26f, 0.38f, 0.56f, 0.80f);
        colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.30f, 0.45f, 0.66f, 1f);
        colors[(int)ImGuiCol.Tab] = new Vector4(0.15f, 0.18f, 0.23f, 1f);
        colors[(int)ImGuiCol.TabHovered] = new Vector4(0.26f, 0.38f, 0.56f, 1f);
        colors[(int)ImGuiCol.TabSelected] = new Vector4(0.22f, 0.33f, 0.48f, 1f);
        colors[(int)ImGuiCol.Separator] = new Vector4(0.25f, 0.27f, 0.32f, 0.70f);
        colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0f, 0f, 0f, 0.55f);
    }

    /// <summary>
    /// Модальное окно по центру с крестиком: фон затемнён, свернуть нельзя.
    /// body возвращает false, когда окно надо закрыть. Возвращает, открыто ли окно после кадра.
    /// </summary>
    private static bool Modal(string id, bool want, Vector2 size, Func<bool> body)
    {
        var isOpen = ImGui.IsPopupOpen(id);
        if (!want && !isOpen)
        {
            return false;
        }
        if (want && !isOpen)
        {
            ImGui.OpenPopup(id);
        }

        var center = new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f);
        ImGui.SetNextWindowPos(center, ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.07f, 0.08f, 0.10f, 1f));
        var keep = want;
        if (ImGui.BeginPopupModal(id, ref keep,
                ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse |
                ImGuiWindowFlags.NoSavedSettings))
        {
            if (want && keep)
            {
                keep = body();
            }
            if (!want || !keep)
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleColor();
        return want && keep;
    }

    /// <summary>Одновременно открыто не больше одного модального окна — по приоритету.</summary>
    private void DrawModals(SimulationHost host)
    {
        var block = false;

        var wantError = host.FatalError is not null;
        Modal("Помилка##error", wantError, new Vector2(480, 0), () => ErrorBody(host));
        block |= wantError;
        if (_host is null)
        {
            return;
        }

        var wantEnd = !block && EndPending(host);
        var endOpen = Modal(UiText.ScenarioOverTitle + "##end", wantEnd, new Vector2(540, 0), () => EndBody(host));
        // крестик: окно закрыто, но сценарий по-прежнему стоит
        if (wantEnd && !endOpen && _host is not null && host.Simulation.Finished)
        {
            host.EndAcknowledged = true;
        }
        block |= wantEnd;
        if (_host is null)
        {
            return;
        }

        var wantPause = !block && _pauseMenu;
        var pauseOpen = Modal(UiText.Pause + "##pause", wantPause, new Vector2(320, 0), () => PauseBody(host));
        if (wantPause)
        {
            if (!pauseOpen && _pauseMenu)
            {
                _pauseMenu = false;
                if (_host is not null)
                {
                    host.Paused = false;
                }
            }
            block = true;
        }
        if (_host is null)
        {
            return;
        }

        var wantTrace = !block && _traceModal;
        var traceOpen = Modal("Виклик нейромережі##trace", wantTrace, new Vector2(760, 600), () => TraceBody(host));
        if (wantTrace)
        {
            _traceModal = traceOpen;
            block = true;
        }

        var wantEvent = !block && _eventModal;
        var eventOpen = Modal(UiText.CreateEvent + "##event", wantEvent, new Vector2(480, 0), () => EventBody(host));
        if (wantEvent)
        {
            if (!eventOpen && _eventModal)
            {
                _eventModal = false;
                host.Paused = false;
            }
            block = true;
        }

        var wantShout = !block && _shoutModal;
        var shoutOpen = Modal(UiText.PlayerShoutTitle + "##shout", wantShout, new Vector2(440, 0), () => ShoutBody(host));
        if (wantShout)
        {
            if (!shoutOpen && _shoutModal)
            {
                _shoutModal = false;
                host.Paused = false;
            }
            block = true;
        }

        var wantHelp = !block && _helpOpen;
        var helpOpen = Modal("Що це за програма і як нею користуватись##help", wantHelp, new Vector2(760, 600),
            () => HelpBody(host));
        if (wantHelp)
        {
            _helpOpen = helpOpen;
        }
    }

    private bool ErrorBody(SimulationHost host)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextWrapped(host.FatalError ?? string.Empty);
        if (!LlmClientFactory.HasApiKey(Config.Llm))
        {
            ImGui.TextDisabled("Схоже, ШІ вимкнено. Введіть ключ у меню або оберіть V0.");
        }
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (ImGui.Button(UiText.Back, new Vector2(140, 28)))
        {
            ToMenu();
            return false;
        }
        return true;
    }

    private bool EndBody(SimulationHost host)
    {
        ImGui.PushTextWrapPos(0);
        ImGui.TextWrapped("Час сценарію «" + host.Scenario.NameUk + "» вийшов (" +
                          host.Simulation.World.Clock.TimeString + "). Усі метрики до цього моменту зафіксовано.");
        ImGui.TextDisabled("Можна дати селу жити далі — розмови і пригоди тривають, але це вже поза експериментом.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
        if (ImGui.Button("Продовжити — світ живе далі", new Vector2(250, 30)))
        {
            host.ContinueBeyondScenario();
            return false;
        }
        ImGui.SameLine();
        if (ImGui.Button(UiText.Restart, new Vector2(130, 30)))
        {
            host.Restart();
            return false;
        }
        ImGui.SameLine();
        if (ImGui.Button(UiText.Back, new Vector2(110, 30)))
        {
            ToMenu();
            return false;
        }
        return true;
    }

    private bool PauseBody(SimulationHost host)
    {
        if (ImGui.Button(UiText.Resume, new Vector2(290, 32)))
        {
            _pauseMenu = false;
            host.Paused = false;
            return false;
        }
        if (ImGui.Button(UiText.Restart, new Vector2(290, 32)))
        {
            _pauseMenu = false;
            host.Restart();
            return false;
        }
        if (ImGui.Button("Довідка", new Vector2(290, 32)))
        {
            _pauseMenu = false;
            _helpOpen = true;
            return false;
        }
        if (ImGui.Button(UiText.Back, new Vector2(290, 32)))
        {
            _pauseMenu = false;
            ToMenu();
            return false;
        }
        return true;
    }

    private bool ShoutBody(SimulationHost host)
    {
        ImGui.TextUnformatted(UiText.ShoutText);
        ImGui.TextDisabled("Крик чують усі поруч, навіть крізь стіну, хоч і гірше.");
        ImGui.SetNextItemWidth(410);
        var submit = ImGui.InputTextWithHint("##shout", "Допоможіть!", ref _shoutText, 200,
            ImGuiInputTextFlags.EnterReturnsTrue);
        submit |= ImGui.Button(UiText.ShoutButton, new Vector2(120, 0));
        if (submit)
        {
            var player = host.Simulation.World.Player;
            host.EmitEvent("shout", Player.PlayerId, null, null,
                host.Simulation.World.ZoneOfTile(player.X, player.Y), player.X, player.Y,
                new Dictionary<string, string> { ["text"] = string.IsNullOrWhiteSpace(_shoutText) ? "Допоможіть!" : _shoutText });
            _shoutModal = false;
            host.Paused = false;
            return false;
        }
        ImGui.SameLine();
        if (ImGui.Button(UiText.Cancel, new Vector2(120, 0)))
        {
            _shoutModal = false;
            host.Paused = false;
            return false;
        }
        return true;
    }

    private bool EventBody(SimulationHost host)
    {
        var types = _panel.EventTypes(host);
        if (types.Length == 0)
        {
            ImGui.TextUnformatted(UiText.NoData);
            if (ImGui.Button(UiText.Close, new Vector2(120, 0)))
            {
                _eventModal = false;
                host.Paused = false;
                return false;
            }
            return true;
        }

        ImGui.TextDisabled("Подія з'явиться в цьому місці. Її побачать або почують ті, хто поруч.");
        _eventTypeIndex = Math.Clamp(_eventTypeIndex, 0, types.Length - 1);
        ImGui.Combo(UiText.EventType, ref _eventTypeIndex, types, types.Length);
        ImGui.InputText(UiText.Actor, ref _eventActor, 64);
        ImGui.InputText(UiText.Target, ref _eventTarget, 64);
        ImGui.InputText(UiText.Item, ref _eventItem, 64);
        ImGui.InputText(UiText.Zone, ref _eventZone, 64);
        ImGui.TextUnformatted(UiText.Tile + ": " + _eventTileX + ", " + _eventTileY);

        if (ImGui.Button(UiText.Create, new Vector2(120, 0)))
        {
            var type = types[_eventTypeIndex];
            host.EmitEvent(type,
                string.IsNullOrWhiteSpace(_eventActor) ? null : _eventActor.Trim(),
                string.IsNullOrWhiteSpace(_eventTarget) ? null : _eventTarget.Trim(),
                string.IsNullOrWhiteSpace(_eventItem) ? null : _eventItem.Trim(),
                string.IsNullOrWhiteSpace(_eventZone) ? null : _eventZone.Trim(),
                _eventTileX, _eventTileY);
            _eventModal = false;
            host.Paused = false;
            return false;
        }
        ImGui.SameLine();
        if (ImGui.Button(UiText.Cancel, new Vector2(120, 0)))
        {
            _eventModal = false;
            host.Paused = false;
            return false;
        }
        return true;
    }

    public void OpenTrace(int index)
    {
        _traceIndex = index;
        _traceModal = true;
    }

    private bool TraceBody(SimulationHost host)
    {
        var traces = host.Simulation.Traces;
        if (_traceIndex < 0 || _traceIndex >= traces.Count)
        {
            ImGui.TextUnformatted(UiText.NoData);
            return !ImGui.Button(UiText.Close, new Vector2(120, 0));
        }

        var trace = traces[_traceIndex];
        ImGui.TextUnformatted("Персонаж: " + trace.NpcName + " (" + trace.NpcId + ") · " + trace.Trigger +
                              " · тік " + trace.Tick);
        ImGui.TextUnformatted("Дія: " + trace.Action + " · " + trace.Verdict +
                              (string.IsNullOrEmpty(trace.RejectReason) ? string.Empty : " (" + trace.RejectReason + ")"));
        ImGui.TextUnformatted("Токени: " + trace.PromptTokens + " -> " + trace.CompletionTokens +
                              " · " + trace.LatencyMs + " мс · $" + trace.CostUsd.ToString("0.000000",
                                  System.Globalization.CultureInfo.InvariantCulture));
        ImGui.Separator();

        ImGui.TextDisabled("Що отримала нейромережа:");
        if (ImGui.BeginChild("##prompt", new Vector2(0, 280), ImGuiChildFlags.Borders))
        {
            ImGui.TextWrapped(trace.Prompt ?? UiText.Empty);
        }
        ImGui.EndChild();

        ImGui.TextDisabled("Що вона відповіла:");
        if (ImGui.BeginChild("##answer", new Vector2(0, 120), ImGuiChildFlags.Borders))
        {
            ImGui.TextWrapped(trace.RawResponse ?? UiText.Empty);
        }
        ImGui.EndChild();

        if (ImGui.Button(UiText.Copy, new Vector2(120, 0)))
        {
            ImGui.SetClipboardText((trace.Prompt ?? string.Empty) + "\n\n" + (trace.RawResponse ?? string.Empty));
            ShowNotice("Промпт і відповідь скопійовано");
        }
        ImGui.SameLine();
        return !ImGui.Button(UiText.Close, new Vector2(120, 0));
    }

    /// <summary>Справка: что это за программа, как самому проверить метод, варианты, легенда, управление.</summary>
    private bool HelpBody(SimulationHost host)
    {
        if (ImGui.BeginChild("##helptext", new Vector2(0, 500), ImGuiChildFlags.None))
        {
            ImGui.PushTextWrapPos(0);
            ImGui.TextWrapped("«Свідок» — дослідницький стенд для диплому, а не гра. У селищі 24 персонажі зі своїми " +
                              "розкладами. Кожен знає лише те, що бачив або чув сам, і переказує це далі з утратою " +
                              "достовірності: c = c0 · r1 · r2 · … Тому чутка слабшає, спотворюється, а персонажі не можуть " +
                              "знати «все».");
            ImGui.Separator();

            ImGui.TextUnformatted("Як перевірити метод самому:");
            HelpBullet("У меню оберіть S6 · Пісочниця, режим «Гравець», варіант V4 і введіть ключ DeepSeek.");
            HelpBullet("Говоріть з людьми (E). Кожен відповідає лише з власних знань.");
            HelpBullet("Вкрадіть кубок на чиїхось очах і розпитайте село: хто знає, хто чув чутку, хто нічого.");
            HelpBullet("Спробуйте обдурити корчмаря Богдана без скрині. Потім те саме з V1 і порівняйте.");
            HelpBullet("Панель «" + UiText.GoalsTitle + "» (F2) веде по кроках і ставить позначки.");
            ImGui.Separator();

            ImGui.TextUnformatted("Варіанти:");
            foreach (var id in new[] { "V0", "V1", "V2", "V3", "V4" })
            {
                HelpBullet(UiText.VariantLabel(id) + ". " + UiText.VariantHint(id));
            }
            ImGui.Separator();

            ImGui.TextUnformatted("Що перевіряють сценарії:");
            HelpBullet("S1 — як розходиться чутка про крадіжку (покриття, T50/T90, середня c, спотворення)");
            HelpBullet("S2 — чи не «всезнає» персонаж: V1 бачить увесь журнал, V4 — лише власні знання");
            HelpBullet("S3 — чи можна обманом змусити корчмаря видати нагороду");
            HelpBullet("S4 — скільки токенів коштує повний журнал проти знань одного персонажа");
            HelpBullet("S5 — як село реагує на пожежу і скільки чекає відповіді моделі");
            HelpBullet("S6 — пісочниця для показу на захисті");
            ImGui.Separator();

            ImGui.TextUnformatted("Кольори персонажів:");
            foreach (var role in Enum.GetValues<Role>())
            {
                var (cr, cg, cb) = Roles.Color(role);
                ImGui.TextColored(new Vector4(Math.Max(cr, (byte)90) / 255f, Math.Max(cg, (byte)90) / 255f,
                    Math.Max(cb, (byte)90) / 255f, 1f), "• " + Roles.NameUk(role));
                ImGui.SameLine();
            }
            ImGui.NewLine();
            ImGui.TextUnformatted("Теплова карта (R) — кільце навколо персонажа:");
            ImGui.TextColored(new Vector4(0.86f, 0.16f, 0.16f, 1f), "• знає впевнено (c ≥ 0.8)");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.90f, 0.78f, 0.12f, 1f), "• сумнівається (c ≥ 0.5)");
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.60f, 0.60f, 0.60f, 1f), "• чув краєм вуха");
            ImGui.TextDisabled("Без кільця — нічого не знає.");
            ImGui.Separator();

            ImGui.TextUnformatted("Керування:");
            ImGui.TextWrapped(host.PlayerMode
                ? "WASD — рух, E — розмова, Q — вкрасти, F — підняти/покласти, G — віддати, Y — крикнути, I — інвентар, " +
                  "R — теплова карта, Tab — панель спостерігача, F2 — підказки, Esc — закрити вікно або пауза."
                : "ЛКМ — вибрати персонажа, ПКМ/СКМ — камера, колесо — масштаб, Пробіл — пауза, 1–5 — швидкість, " +
                  "V/H — радіуси зору і слуху, T — довіра, R — теплова карта, G — сітка, L — імена, " +
                  "Ctrl+ЛКМ — створити подію, F2 — підказки, Esc — закрити вікно або пауза.");
            ImGui.TextDisabled("Персонажі — кольорові кола: графіка навмисно проста, це інструмент для експериментів.");
            ImGui.PopTextWrapPos();
        }
        ImGui.EndChild();

        return !ImGui.Button(UiText.Close, new Vector2(140, 28));
    }

    private static void HelpBullet(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }

    private void StartSimulation(MenuResult result)
    {
        _host?.Dispose();
        _host = new SimulationHost(result.Scenario, Config, Catalog, result.Variant, result.Seed, result.PlayerMode, Tokens,
            result.Profile);
        _playerController = new PlayerController(_host);
        _showPanel = !result.PlayerMode || _options.ForcePanel;
        _host.SpeedIndex = _options.Speed;
        _host.Paused = _options.Speed == 0;
        Screen = AppScreen.Simulation;
        _pauseMenu = false;
        _helpOpen = _options.ShowHelp;
        _goalsOpen = _options.MaxFrames == 0;
        ShowNotice(result.PlayerMode
            ? "Режим гравця: підійдіть до когось і натисніть E. Підказки — ліворуч (F2)."
            : "Спостерігач: клікніть на персонажа, R — теплова карта. Підказки — ліворуч (F2).");

        if (result.Variant != Variant.V0 && Config.Llm.ResolveMode() != LlmMode.Mock &&
            !LlmClientFactory.HasApiKey(Config.Llm))
        {
            ShowNotice(UiText.NoApiKey + ": " + Config.Llm.ApiKeyEnv);
        }
    }

    public void RestartSimulation()
    {
        _host?.Restart();
    }

    public void ToMenu()
    {
        _host?.Dispose();
        _host = null;
        Screen = AppScreen.Menu;
    }

    public void Quit() => _quit = true;

    /// <summary>
    /// Шрифт для ImGui с кириллицей и типографскими знаками. Вызывается ДО rlImGui.Setup,
    /// потому что атлас шрифтов собирается один раз и добавление после сборки не даёт глифов.
    /// </summary>
    private void PrepareUiFont()
    {
        var path = PathResolver.ResolveExisting(Config.Ui.Font);
        if (!File.Exists(path))
        {
            Log.Warn("Не знайдено шрифт " + path + ", буде стандартний шрифт ImGui");
            return;
        }

        if (_glyphRangesPtr == IntPtr.Zero)
        {
            var ranges = new ushort[]
            {
                0x0020, 0x00FF,
                0x0400, 0x04FF,
                0x2000, 0x206F,
                0x2116, 0x2116,
                0x2190, 0x21FF,
                0
            };
            var shorts = new short[ranges.Length];
            for (var i = 0; i < ranges.Length; i++)
            {
                shorts[i] = unchecked((short)ranges[i]);
            }
            _glyphRangesPtr = Marshal.AllocHGlobal(ranges.Length * sizeof(ushort));
            Marshal.Copy(shorts, 0, _glyphRangesPtr, shorts.Length);
        }

        var size = (float)Math.Max(12, Config.Ui.UiFontPx);
        rlImGui.LoadDefaultFont = false;
        rlImGui.LoadFontAwesome = false;
        rlImGui.SetupUserFonts = io =>
        {
            io.Fonts.AddFontFromFileTTF(path, size, default, _glyphRangesPtr);
        };
    }

    /// <summary>Raylib не вміє писати у шлях з кирилицею, тому знімок робиться у робочу теку й переноситься .NET.</summary>
    private static void SaveScreenshot(string path)
    {
        const string tempName = "svidok_screenshot_tmp.png";
        Raylib.TakeScreenshot(tempName);
        var source = Path.GetFullPath(tempName);
        if (!File.Exists(source))
        {
            Log.Warn("Скриншот не створено: " + source);
            return;
        }
        var target = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.Copy(source, target, true);
        File.Delete(source);
        Log.Info("Скриншот збережено: " + target);
    }

    private void AutoStart()
    {
        var scenario = ScenarioLoader.FindById(_options.ScenarioId!);
        if (scenario is null)
        {
            Log.Error("Автостарт: невідомий сценарій " + _options.ScenarioId);
            return;
        }
        var playerMode = _options.PlayerMode && scenario.SupportsMode("Player");
        StartSimulation(new MenuResult(scenario, _options.Variant, _options.Seed, playerMode));
        _panel.TabIndex = _options.TabIndex;

        if (_options.OpenDialogNpc is not null && _host is not null)
        {
            var npc = _host.Simulation.World.Npc(_options.OpenDialogNpc);
            if (npc is not null)
            {
                _host.OpenDialog(npc);
                _host.ShowTechDetails = _options.TechDetails;
                if (!string.IsNullOrWhiteSpace(_options.SayText))
                {
                    _host.SendDialogText(_options.SayText!);
                }
            }
        }
    }

    private Font LoadMapFont()
    {
        var path = PathResolver.ResolveExisting(Config.Ui.Font);
        if (!File.Exists(path))
        {
            return Raylib.GetFontDefault();
        }

        var codepoints = new List<int>();
        for (var c = 0x20; c <= 0x7E; c++)
        {
            codepoints.Add(c);
        }
        for (var c = 0x400; c <= 0x4FF; c++)
        {
            codepoints.Add(c);
        }
        foreach (var extra in new[] { 0x00AB, 0x00BB, 0x2018, 0x2019, 0x2013, 0x2014, 0x2026, 0x2116 })
        {
            codepoints.Add(extra);
        }

        _customFont = true;
        var font = Raylib.LoadFontEx(path, 32, codepoints.ToArray(), codepoints.Count);
        Raylib.SetTextureFilter(font.Texture, TextureFilter.Point);
        return font;
    }

    public void Dispose()
    {
        _host?.Dispose();
        _host = null;
    }
}

/// <summary>Небольшой помощник, чтобы не тянуть Core-типы в разметку.</summary>
internal static class Deciding
{
    public static string VariantName(SimulationHost host) => Variants.NameUk(host.Simulation.Variant);
}

internal sealed record ScenarioDefinitionCache(string Id);
