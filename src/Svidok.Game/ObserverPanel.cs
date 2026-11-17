using System.Globalization;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.WorldModel;

namespace Svidok.Game;

/// <summary>Правая панель наблюдателя: персонаж, журнал, слухи, нейросеть, статистика (раздел 12).</summary>
public sealed class ObserverPanel
{
    private bool _hideBackground = true;
    private bool _filterReady;
    private bool _userPickedEvent;
    private string[]? _types;
    private readonly HashSet<string> _journalFilter = new(StringComparer.Ordinal);
    private int _sortColumn = 3;
    private bool _sortAsc;
    private int _eventComboIndex;
    private List<WorldEvent> _importantEvents = new();
    private int _importantRefreshTick = -1;
    private string _statsCache = string.Empty;
    private int _statsCacheTick = -1;
    private int _tabIndex;
    private bool _tabsInitialized;

    /// <summary>Стартовая вкладка (0..4): ставится из параметров запуска или при перезапуске сценария.</summary>
    public int TabIndex
    {
        get => _tabIndex;
        set
        {
            _tabIndex = Math.Clamp(value, 0, 4);
            _tabsInitialized = false;
        }
    }

    public string[] EventTypes(SimulationHost host)
    {
        return _types ??= host.Simulation.World.Schema.Types.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
    }

    public void Draw(GameApp app, SimulationHost host)
    {
        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        ImGui.SetNextWindowPos(new Vector2(width - 480, 36), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(480, height - 60), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(8, 8));
        if (ImGui.Begin("##panel", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize |
                                      ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar))
        {
            if (ImGui.BeginTabBar("##tabs", ImGuiTabBarFlags.AutoSelectNewTabs))
            {
                var open = true;
                if (ImGui.BeginTabItem(UiText.TabCharacter, ref open, TabFlags(0)))
                {
                    DrawCharacter(app, host);
                    ImGui.EndTabItem();
                }
                open = true;
                if (ImGui.BeginTabItem(UiText.TabJournal, ref open, TabFlags(1)))
                {
                    DrawJournal(app, host);
                    ImGui.EndTabItem();
                }
                open = true;
                if (ImGui.BeginTabItem(UiText.TabRumors, ref open, TabFlags(2)))
                {
                    DrawRumors(app, host);
                    ImGui.EndTabItem();
                }
                open = true;
                if (ImGui.BeginTabItem(UiText.TabLlm, ref open, TabFlags(3)))
                {
                    DrawLlm(app, host);
                    ImGui.EndTabItem();
                }
                open = true;
                if (ImGui.BeginTabItem(UiText.TabStats, ref open, TabFlags(4)))
                {
                    DrawStats(app, host);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
                _tabsInitialized = true;
            }
        }
        ImGui.End();
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// Стартовую вкладку выбираем только один раз. Если передавать SetSelected каждый кадр,
    /// панель будет возвращать пользователя на «Персонаж» после любого клика.
    /// </summary>
    private ImGuiTabItemFlags TabFlags(int index) =>
        !_tabsInitialized && _tabIndex == index ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;

    private void DrawCharacter(GameApp app, SimulationHost host)
    {
        var world = host.Simulation.World;
        var npc = host.SelectedNpc;
        if (npc is null)
        {
            ImGui.TextUnformatted("Клікніть на персонажа на карті.");
            return;
        }

        ImGui.TextUnformatted(npc.Name + " · " + Roles.NameUk(npc.Role));
        ImGui.TextDisabled("Зона: " + world.ZoneName(world.ZoneOfTile(npc.X, npc.Y)) +
                           " · Заняття: " + Activities.NameUk(npc.Activity));
        if (npc.CurrentAction is { } action)
        {
            var actionName = string.IsNullOrEmpty(action.NameUk) ? action.ActionId : action.NameUk;
            ImGui.TextUnformatted(action.TargetId is null
                ? "Дія: " + actionName
                : "Дія: " + actionName + " -> " + world.NpcName(action.TargetId));
        }
        else if (npc.IsInConversation)
        {
            ImGui.TextUnformatted("Розмова з " + world.NpcName(npc.ConversationPartnerId));
        }

        if (ImGui.Button(UiText.AskLlm, new Vector2(220, 26)))
        {
            host.Simulation.RequestManual(npc);
            app.ShowNotice("Запит до моделі для " + npc.Name);
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Довіра (r):");
        DrawTrustRow(world, npc, world.Trust.TopTrusted(npc.Id, 5));
        ImGui.TextDisabled("найменша:");
        DrawTrustRow(world, npc, world.Trust.LeastTrusted(npc.Id, 5));
        ImGui.TextUnformatted("До гравця: " +
                              world.Trust.Get(npc.Id, Player.PlayerId).ToString("0.00", CultureInfo.InvariantCulture));

        ImGui.Separator();
        ImGui.Checkbox("Сховати фонові", ref _hideBackground);

        var entries = npc.Knowledge.Entries.AsEnumerable();
        if (_hideBackground)
        {
            entries = entries.Where(e => !world.Schema.Get(e.Type).Background);
        }
        var list = SortEntries(entries.ToList());

        if (ImGui.BeginTable("##knowledge", 7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY |
                ImGuiTableFlags.ScrollX | ImGuiTableFlags.Resizable,
                new Vector2(0, 260)))
        {
            ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 34);
            ImGui.TableSetupColumn("Подія", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Джерело", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("c", ImGuiTableColumnFlags.WidthFixed, 34);
            ImGui.TableSetupColumn("Ланцюг", ImGuiTableColumnFlags.WidthFixed, 70);
            ImGui.TableSetupColumn("Спотв.", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Коли", ImGuiTableColumnFlags.WidthFixed, 44);

            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
            DrawSortHeader(0, "#");
            DrawSortHeader(1, "Подія");
            DrawSortHeader(2, "Джерело");
            DrawSortHeader(3, "c");
            DrawSortHeader(4, "Ланцюг");
            DrawSortHeader(5, "Спотв.");
            DrawSortHeader(6, "Коли");

            foreach (var entry in list)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                var selected = host.SelectedEventId is int eventId && entry.EventId == eventId;
                if (!host.LockEventSelection && ImGui.Selectable("#" + entry.PromptRef, selected))
                {
                    host.SelectedEventId = entry.EventId;
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(world.Schema.Get(entry.Type).NameUk);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(Sources.NameUk(entry.Source));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.C.ToString("0.00", CultureInfo.InvariantCulture));
                ImGui.TableNextColumn();
                var chainText = entry.Chain.Count <= 1
                    ? world.NpcName(entry.OriginId)
                    : string.Join(" -> ", entry.Chain.Select(world.NpcName));
                ImGui.TextUnformatted(chainText);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Ланцюг: " + chainText +
                                   "\nДжерело: " + Sources.NameUk(entry.Source) +
                                   "\nc = " + entry.C.ToString("0.00", CultureInfo.InvariantCulture) +
                                   (entry.IsDistorted ? "\nспотворено" : string.Empty));
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(entry.IsDistorted ? "так" : "ні");
                ImGui.TableNextColumn();
                var truth = entry.EventId is int id ? world.Log.ById(id) : null;
                ImGui.TextUnformatted(truth is null ? "—" : ShortTime(world, truth.Tick));
            }
            ImGui.EndTable();
        }

        if (host.SelectedEventId is int selectedId && npc.Knowledge.ByEventId(selectedId) is { } selectedEntry)
        {
            ImGui.Separator();
            DrawTruthAndKnowledge(world, selectedEntry);
        }
    }

    private void DrawSortHeader(int column, string label)
    {
        ImGui.TableNextColumn();
        var marker = _sortColumn == column ? (_sortAsc ? " ^" : " v") : string.Empty;
        if (ImGui.SmallButton(label + marker))
        {
            if (_sortColumn == column)
            {
                _sortAsc = !_sortAsc;
            }
            else
            {
                _sortColumn = column;
                _sortAsc = false;
            }
        }
    }

    private List<KnowledgeEntry> SortEntries(List<KnowledgeEntry> entries)
    {
        Comparison<KnowledgeEntry> comparison = _sortColumn switch
        {
            0 => (a, b) => a.PromptRef.CompareTo(b.PromptRef),
            1 => (a, b) => string.CompareOrdinal(a.Type, b.Type),
            2 => (a, b) => a.Source.CompareTo(b.Source),
            3 => (a, b) => a.C.CompareTo(b.C),
            4 => (a, b) => a.Chain.Count.CompareTo(b.Chain.Count),
            5 => (a, b) => a.IsDistorted.CompareTo(b.IsDistorted),
            _ => (a, b) => (a.EventId ?? 0).CompareTo(b.EventId ?? 0)
        };
        entries.Sort(comparison);
        if (!_sortAsc)
        {
            entries.Reverse();
        }
        return entries;
    }

    private static void DrawTrustRow(World world, Npc npc, IReadOnlyList<(string Id, double R)> rows)
    {
        foreach (var (id, r) in rows)
        {
            ImGui.TextUnformatted("  " + world.NpcName(id) + " — " + r.ToString("0.00", CultureInfo.InvariantCulture));
        }
    }

    private static void DrawTruthAndKnowledge(World world, KnowledgeEntry entry)
    {
        var truth = entry.EventId is int id ? world.Log.ById(id) : null;
        ImGui.TextUnformatted(UiText.Truth + ":");
        if (truth is null)
        {
            ImGui.TextDisabled("події немає в журналі (вигадка)");
        }
        else
        {
            ImGui.TextWrapped(DescribeEvent(world, truth));
        }

        ImGui.TextUnformatted(UiText.WhatNpcKnows + ":");
        foreach (var pair in entry.Fields)
        {
            var truthValue = truth is null ? null : TruthValue(world, truth, pair.Key);
            var differs = truthValue is not null && !string.Equals(truthValue, pair.Value, StringComparison.Ordinal);
            if (differs)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.35f, 0.3f, 1f));
            }
            var suffix = differs ? " (правда: " + truthValue + ")" : string.Empty;
            ImGui.TextWrapped("  " + FieldLabel(pair.Key) + " = " + RenderValue(world, pair.Key, pair.Value) + suffix);
            if (differs)
            {
                ImGui.PopStyleColor();
            }
        }
        ImGui.TextDisabled("c = " + entry.C.ToString("0.00", CultureInfo.InvariantCulture) +
                           " · джерело: " + Sources.NameUk(entry.Source) +
                           " · хибне: " + (entry.IsFalse ? "так" : "ні"));
    }

    private static string FieldLabel(string key) => key switch
    {
        "actor" => "хто",
        "target" => "чиє",
        "item" => "що",
        "zone" => "де",
        "price" => "ціна",
        "coins" => "монети",
        "text" => "текст",
        "questId" => "завдання",
        "reason" => "причина",
        _ => key
    };

    private static string RenderValue(World world, string key, string value) => key switch
    {
        "actor" or "target" => world.NpcName(value),
        "item" => world.ItemName(value),
        "zone" => world.ZoneName(value),
        _ => value
    };

    private static string? TruthValue(World world, WorldEvent worldEvent, string key)
    {
        var value = key switch
        {
            "actor" => worldEvent.ActorId,
            "target" => worldEvent.TargetId,
            "item" => worldEvent.ItemId,
            "zone" => worldEvent.ZoneId,
            _ => worldEvent.Extra.TryGetValue(key, out var extra) ? extra : null
        };
        return value is null ? null : RenderValue(world, key, value);
    }

    public static string DescribeEvent(World world, WorldEvent worldEvent)
    {
        var parts = new List<string>
        {
            world.Schema.Get(worldEvent.Type).NameUk,
            "коли: " + world.TimeOf(worldEvent.Tick)
        };
        if (worldEvent.ActorId is not null)
        {
            parts.Add("хто: " + world.NpcName(worldEvent.ActorId));
        }
        if (worldEvent.TargetId is not null)
        {
            parts.Add("кого: " + world.NpcName(worldEvent.TargetId));
        }
        if (worldEvent.ItemId is not null)
        {
            parts.Add("предмет: " + world.ItemName(worldEvent.ItemId));
        }
        if (worldEvent.ZoneId is not null)
        {
            parts.Add("де: " + world.ZoneName(worldEvent.ZoneId));
        }
        foreach (var pair in worldEvent.Extra)
        {
            parts.Add(FieldLabel(pair.Key) + ": " + pair.Value);
        }
        return string.Join(" · ", parts);
    }

    private void DrawJournal(GameApp app, SimulationHost host)
    {
        var world = host.Simulation.World;
        if (!_filterReady)
        {
            _filterReady = true;
            foreach (var type in EventTypes(host))
            {
                _journalFilter.Add(type);
            }
        }

        ImGui.Checkbox("Сховати фонові", ref _hideBackground);
        ImGui.SameLine();
        if (ImGui.SmallButton("Усі"))
        {
            foreach (var type in EventTypes(host))
            {
                _journalFilter.Add(type);
            }
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Жодного"))
        {
            _journalFilter.Clear();
        }

        var types = EventTypes(host);
        var selectedTypes = types.Count(_journalFilter.Contains);
        if (ImGui.BeginCombo("##filters", "Типів: " + selectedTypes + " з " + types.Length))
        {
            foreach (var type in types)
            {
                var enabled = _journalFilter.Contains(type);
                if (ImGui.Checkbox(world.Schema.Get(type).NameUk + "##f_" + type, ref enabled))
                {
                    if (enabled)
                    {
                        _journalFilter.Add(type);
                    }
                    else
                    {
                        _journalFilter.Remove(type);
                    }
                }
            }
            ImGui.EndCombo();
        }

        var events = world.Log.Events
            .Where(e => _journalFilter.Contains(e.Type))
            .Where(e => !_hideBackground || !e.IsBackground)
            .TakeLast(400)
            .Reverse()
            .ToList();

        if (ImGui.BeginTable("##journal", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX,
                new Vector2(0, 320)))
        {
            ImGui.TableSetupColumn("Час", ImGuiTableColumnFlags.WidthFixed, 74);
            ImGui.TableSetupColumn("Тип", ImGuiTableColumnFlags.WidthFixed, 86);
            ImGui.TableSetupColumn("Хто", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Кого/що", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Де", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("Знає", ImGuiTableColumnFlags.WidthFixed, 36);
            ImGui.TableHeadersRow();

            foreach (var worldEvent in events)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (!host.LockEventSelection && ImGui.Selectable(world.TimeOf(worldEvent.Tick).Replace("День ", "Д") + "##ev" + worldEvent.Id, false))
                {
                    host.SelectedEventId = worldEvent.Id;
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(world.Schema.Get(worldEvent.Type).NameUk);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(world.NpcName(worldEvent.ActorId));
                ImGui.TableNextColumn();
                var target = worldEvent.TargetId is null
                    ? world.ItemName(worldEvent.ItemId)
                    : world.NpcName(worldEvent.TargetId);
                ImGui.TextUnformatted(target);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(world.ZoneName(worldEvent.ZoneId));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(KnowsCount(world, worldEvent.Id).ToString());
            }
            ImGui.EndTable();
        }

        var background = world.Log.Events.Count(e => e.IsBackground);
        ImGui.TextDisabled(UiText.Total + ": " + world.Log.Count + " · " + UiText.Background + ": " + background +
                           " · " + UiText.LogSize + ": " +
                           (world.Log.ByteSize / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " КБ");
    }

    private static string ShortTime(World world, int tick)
    {
        var absolute = world.Clock.StartMinuteOfDay + tick;
        var minute = absolute % 1440;
        return (minute / 60).ToString("00") + ":" + (minute % 60).ToString("00");
    }

    private static int KnowsCount(World world, int eventId)
    {
        var count = 0;
        foreach (var npc in world.Npcs)
        {
            if (npc.Knowledge.ByEventId(eventId) is not null)
            {
                count++;
            }
        }
        return count;
    }

    private void DrawRumors(GameApp app, SimulationHost host)
    {
        var world = host.Simulation.World;
        if (_importantRefreshTick != world.Log.Count)
        {
            _importantRefreshTick = world.Log.Count;
            _importantEvents = world.Log.Events
                .Where(e => !e.IsBackground && e.Importance >= 0.4)
                .TakeLast(40)
                .Reverse()
                .ToList();
        }

        if (_importantEvents.Count == 0)
        {
            ImGui.TextUnformatted("Ще немає важливих подій.");
            return;
        }

        _eventComboIndex = Math.Clamp(_eventComboIndex, 0, _importantEvents.Count - 1);
        if (!_userPickedEvent && host.Simulation.Metrics.S1TargetEventId is int targetId)
        {
            var targetIndex = _importantEvents.FindIndex(e => e.Id == targetId);
            if (targetIndex >= 0 && _eventComboIndex != targetIndex)
            {
                _eventComboIndex = targetIndex;
            }
        }

        var labels = _importantEvents
            .Select(e => "#" + e.Id + " " + world.Schema.Get(e.Type).NameUk + " · " + world.TimeOf(e.Tick))
            .ToArray();
        if (ImGui.Combo("Подія", ref _eventComboIndex, labels, labels.Length))
        {
            _userPickedEvent = true;
        }
        var selected = _importantEvents[_eventComboIndex];
        host.SelectedEventId ??= selected.Id;

        var participants = new[] { selected.ActorId, selected.TargetId }
            .Where(id => !string.IsNullOrEmpty(id))
            .ToHashSet(StringComparer.Ordinal);

        var observers = world.ActiveNpcs().Where(n => !participants.Contains(n.Id)).ToList();
        var known = observers
            .Select(n => n.Knowledge.ByEventId(selected.Id))
            .Where(e => e is not null)
            .Cast<KnowledgeEntry>()
            .ToList();

        var observerCount = Math.Max(1, observers.Count);
        var coverage = (double)known.Count / observerCount;
        var meanC = known.Count > 0 ? known.Average(e => e.C) : 0;
        var distorted = known.Count > 0 ? (double)known.Count(e => e.IsDistorted) / known.Count : 0;

        ImGui.TextUnformatted(UiText.Coverage + ": " + coverage.ToString("0.00", CultureInfo.InvariantCulture) +
                              "  ·  " + UiText.MeanC + ": " + meanC.ToString("0.00", CultureInfo.InvariantCulture));
        ImGui.TextUnformatted(UiText.Distorted + ": " + distorted.ToString("0.00", CultureInfo.InvariantCulture));

        var summary = host.Simulation.Metrics.S1Summary;
        if (host.Simulation.Metrics.S1TargetEventId == selected.Id)
        {
            ImGui.TextUnformatted(UiText.T50 + ": " + (summary.T50?.ToString() ?? UiText.NotReached));
            ImGui.SameLine();
            ImGui.TextUnformatted(UiText.T90 + ": " + (summary.T90?.ToString() ?? UiText.NotReached));
        }

        var history = host.Simulation.Metrics.CoverageHistory.OrderBy(p => p.Key).Select(p => (float)p.Value).ToArray();
        if (history.Length > 1)
        {
            ImGui.PlotLines("##coverage", ref history[0], history.Length, 0, UiText.Coverage, 0f, 1f, new Vector2(450, 110));
        }
        else
        {
            ImGui.TextDisabled(UiText.NoData);
        }

        ImGui.TextDisabled("R — теплова карта на карті, T — лінії довіри");

        if (ImGui.CollapsingHeader("Список знаючих"))
        {
            foreach (var entry in known.OrderByDescending(e => e.C))
            {
                var npc = observers.FirstOrDefault(n => ReferenceEquals(n.Knowledge.ByEventId(selected.Id), entry));
                ImGui.TextUnformatted((npc?.Name ?? "?") + " — c=" +
                                      entry.C.ToString("0.00", CultureInfo.InvariantCulture) +
                                      (entry.IsDistorted ? " · спотворено" : string.Empty));
            }
        }
    }

    private void DrawLlm(GameApp app, SimulationHost host)
    {
        var traces = host.Simulation.Traces;
        var metrics = host.Simulation.Metrics;

        ImGui.TextUnformatted(UiText.Queue + ": " + host.Simulation.Queue.ReadyCount +
                              " · " + UiText.ActiveRequests + ": " + host.Simulation.ActiveRequests);

        if (ImGui.BeginTable("##calls", 8,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.ScrollX,
                new Vector2(0, 300)))
        {
            ImGui.TableSetupColumn("Час", ImGuiTableColumnFlags.WidthFixed, 38);
            ImGui.TableSetupColumn("NPC", ImGuiTableColumnFlags.WidthFixed, 58);
            ImGui.TableSetupColumn("Вхід", ImGuiTableColumnFlags.WidthFixed, 44);
            ImGui.TableSetupColumn("Кеш", ImGuiTableColumnFlags.WidthFixed, 36);
            ImGui.TableSetupColumn("Вихід", ImGuiTableColumnFlags.WidthFixed, 42);
            ImGui.TableSetupColumn("мс", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Дія", ImGuiTableColumnFlags.WidthFixed, 64);
            ImGui.TableSetupColumn("Вердикт", ImGuiTableColumnFlags.WidthFixed, 56);
            ImGui.TableHeadersRow();

            for (var i = traces.Count - 1; i >= 0 && i >= traces.Count - 80; i--)
            {
                var trace = traces[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable("Д" + trace.Tick + "##t" + i, false))
                {
                    app.OpenTrace(i);
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(trace.NpcName);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(trace.PromptTokens.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(trace.FromCache ? "так" : "ні");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(trace.CompletionTokens.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(trace.LatencyMs.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(trace.Action);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(trace.Verdict);
            }
            ImGui.EndTable();
        }

        var latencies = metrics.Latencies;
        var average = latencies.Count > 0 ? latencies.Average() : 0;
        ImGui.TextUnformatted(UiText.Requests + ": " + metrics.LlmCount +
                              " · " + UiText.AvgLatency + ": " +
                              average.ToString("0", CultureInfo.InvariantCulture) + " мс");
        ImGui.TextUnformatted("Токени: " + metrics.PromptTokens + " -> " + metrics.CompletionTokens +
                              " · " + UiText.Cost + ": $" +
                              metrics.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture));
        ImGui.TextUnformatted(UiText.Rejections + ": " + metrics.Rejections +
                              " · " + UiText.ParseErrors + ": " + metrics.ParseErrors);
    }

    private void DrawStats(GameApp app, SimulationHost host)
    {
        var simulation = host.Simulation;
        var world = simulation.World;
        var metrics = simulation.Metrics;

        if (_statsCacheTick != world.Log.Count)
        {
            _statsCacheTick = world.Log.Count;
            var json = world.Log.ToJsonBytes();
            var text = System.Text.Encoding.UTF8.GetString(json);
            var tokens = simulation.EstimateTokens(text);
            var npcForPrompt = host.SelectedNpc ?? world.Npcs[0];
            var npcTokens = simulation.NpcPromptBuilder is null
                ? "—"
                : simulation.EstimateTokens(simulation.NpcPromptBuilder(npcForPrompt)).ToString();
            _statsCache = "Подій: " + world.Log.Count +
                          "\nРозмір журналу: " + (json.LongLength / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " КБ" +
                          "\nТокенів повного журналу: " + tokens +
                          "\nТокенів промпта NPC (" + npcForPrompt.Name + "): " + npcTokens;
        }

        ImGui.TextUnformatted(_statsCache);
        ImGui.Separator();
        ImGui.TextUnformatted("NPC: " + world.Npcs.Count + " · " + world.Clock.TimeString);
        ImGui.TextUnformatted("Запитів: " + metrics.LlmCount +
                              " · успішних розборів: " + (metrics.LlmCount - metrics.ParseErrors));
        ImGui.TextUnformatted("Відмов валідатора: " + metrics.Rejections);
        ImGui.TextUnformatted("Вартість: $" + metrics.CostUsd.ToString("0.0000", CultureInfo.InvariantCulture));
        var latencies = metrics.Latencies;
        if (latencies.Count > 0)
        {
            ImGui.TextUnformatted("Затримка p50: " +
                                  Percentile(latencies, 0.5).ToString("0", CultureInfo.InvariantCulture) +
                                  " мс · p95: " + Percentile(latencies, 0.95).ToString("0", CultureInfo.InvariantCulture) + " мс");
        }

        ImGui.Separator();
        if (ImGui.Button(UiText.ExportCsv, new Vector2(200, 28)))
        {
            host.ExportCsv();
        }
    }

    private static double Percentile(IReadOnlyList<long> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }
        var sorted = values.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}
