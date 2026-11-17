using System.Globalization;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Quests;

namespace Svidok.Game;

/// <summary>Окно диалога игрока и инвентарь (раздел 11.4).</summary>
public static class DialogWindow
{
    private static readonly string[] QuickQuestions =
    [
        "Що чув сьогодні?",
        "Бачив щось дивне?",
        "Що знаєш про срібний кубок?",
        "Я виконав завдання, де нагорода?"
    ];

    private static readonly Vector4 PlayerColor = new(0.55f, 0.75f, 1.00f, 1f);
    private static readonly Vector4 ActionColor = new(0.95f, 0.80f, 0.35f, 1f);
    private static readonly Vector4 HintColor = new(0.95f, 0.80f, 0.35f, 1f);

    private static readonly Vector4 BlockedColor = new(0.95f, 0.45f, 0.40f, 1f);
    private static readonly Vector4 GoodColor = new(0.45f, 0.85f, 0.50f, 1f);
    private static readonly Vector4 BadColor = new(0.95f, 0.45f, 0.40f, 1f);

    public static void Draw(GameApp app, SimulationHost host)
    {
        if (host.ShowInventory)
        {
            DrawInventory(app, host);
        }

        var dialog = host.Dialog;
        if (dialog is null)
        {
            return;
        }

        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        var npc = dialog.Npc;
        var sim = host.Simulation;
        var world = sim.World;
        var (cr, cg, cb) = Roles.Color(npc.Role);
        var npcColor = new Vector4(Math.Max(cr, (byte)90) / 255f, Math.Max(cg, (byte)90) / 255f, Math.Max(cb, (byte)90) / 255f, 1f);
        var memory = host.MemoryOf(npc);

        var areaWidth = width - (app.ShowPanel ? 480 : 0);
        var windowWidth = Math.Min(760f, areaWidth - 16f);
        const float windowHeight = 330f;
        ImGui.SetNextWindowPos(new Vector2(Math.Max(8, areaWidth / 2f - windowWidth / 2f), height - 24 - windowHeight - 6),
            ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(windowWidth, windowHeight), ImGuiCond.Appearing);
        ImGui.SetNextWindowSizeConstraints(new Vector2(420, 220), new Vector2(width - 16, height - 80));
        ImGui.SetNextWindowBgAlpha(0.92f);

        var open = true;
        if (ImGui.Begin(npc.Name + " · " + Roles.NameUk(npc.Role) + "##dialog", ref open,
                ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings))
        {
            DrawHeader(host, npc, memory);

            var hasDemand = host.Simulation.World.Game.Demands.ContainsKey(npc.Id);
            var deedRows = host.Simulation.World.Game.Social ? 1 : 0;
            var blockedReason = host.CannotTalkReason(npc);
            var footer = ImGui.GetFrameHeightWithSpacing() * (2 + deedRows + (hasDemand ? 1 : 0) + (blockedReason is null ? 0 : 1)) + 6;
            if (ImGui.BeginChild("##history", new Vector2(0, -footer), ImGuiChildFlags.Borders))
            {
                if (dialog.Lines.Count == 0)
                {
                    ImGui.TextDisabled("Напишіть щось унизу й натисніть Enter. " + npc.Name + " відповідає лише з того, що сам бачив або чув.");
                }

                for (var i = 0; i < dialog.Lines.Count; i++)
                {
                    var line = dialog.Lines[i];
                    if (line.IsPlayer)
                    {
                        ImGui.TextColored(PlayerColor, host.PlayerName + ":");
                    }
                    else
                    {
                        ImGui.TextColored(npcColor, npc.Name + ":");
                    }
                    ImGui.SameLine();
                    ImGui.TextWrapped(line.Text);
                    if (!line.IsPlayer && dialog.LineActions.TryGetValue(i, out var actionId))
                    {
                        ImGui.TextColored(ActionColor, "   (" + npc.Name + " " + UiText.ActionNameUk(actionId) + ")");
                    }
                }

                if (dialog.Pending is not null)
                {
                    ImGui.TextDisabled(npc.Name + " думає…");
                }
                if (dialog.ScrollToEnd || ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 1)
                {
                    ImGui.SetScrollHereY(1.0f);
                    dialog.ScrollToEnd = false;
                }
            }
            ImGui.EndChild();

            var pending = dialog.Pending is not null;
            var blocked = pending || blockedReason is not null;
            if (blockedReason is not null)
            {
                ImGui.TextColored(BlockedColor, blockedReason);
            }
            if (hasDemand)
            {
                DrawDemand(host, npc, blocked);
            }
            ImGui.BeginDisabled(blockedReason is not null);
            if (dialog.FocusInput && blockedReason is null)
            {
                ImGui.SetKeyboardFocusHere();
                dialog.FocusInput = false;
            }
            ImGui.SetNextItemWidth(-100);
            var hint = blockedReason is not null ? "Зараз поговорити не вийде"
                : pending ? npc.Name + " ще відповідає…"
                : "Ваша репліка, Enter — надіслати";
            var send = ImGui.InputTextWithHint("##say", hint, ref dialog.Input, 300, ImGuiInputTextFlags.EnterReturnsTrue);
            if (send)
            {
                ImGui.SetKeyboardFocusHere(-1);
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(blocked);
            send |= ImGui.Button("Надіслати", new Vector2(92, 0));
            ImGui.EndDisabled();
            if (send && !blocked)
            {
                host.SendDialogText(dialog.Input);
                dialog.ScrollToEnd = true;
            }

            ImGui.BeginDisabled(blocked);
            for (var i = 0; i < QuickQuestions.Length; i++)
            {
                if (i > 0)
                {
                    ImGui.SameLine();
                }
                if (ImGui.SmallButton(QuickQuestions[i] + "##quick" + i))
                {
                    host.SendDialogText(QuickQuestions[i]);
                    dialog.ScrollToEnd = true;
                    dialog.FocusInput = true;
                }
            }
            ImGui.EndDisabled();
            ImGui.EndDisabled();

            if (host.Simulation.World.Game.Social)
            {
                DrawDeeds(host, npc, blocked);
            }
            ImGui.BeginDisabled(blockedReason is not null);
            DrawActions(app, host, npc);
            ImGui.EndDisabled();
        }
        ImGui.End();

        if (!open)
        {
            host.CloseDialog();
        }
    }

    private static void DrawHeader(SimulationHost host, Npc npc, NpcMemory memory)
    {
        var world = host.Simulation.World;
        var trust = world.Trust.Get(npc.Id, Player.PlayerId);
        ImGui.TextDisabled(world.ZoneName(npc.WorkZone));
        ImGui.SameLine();
        ImGui.TextDisabled("·");
        ImGui.SameLine();
        ImGui.TextColored(TrustColor(trust), "довіра до вас " + trust.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        var change = host.Dialog?.LastAttitude ?? 0;
        if (change != 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(change > 0 ? GoodColor : BadColor, change > 0 ? "(стала краще)" : "(стала гірше)");
        }
        if (memory.Talks > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled("· розмов: " + memory.Talks);
        }

        if (host.Simulation.Variant == Variant.V0)
        {
            ImGui.SameLine();
            ImGui.TextColored(HintColor, "· V0: заготовлені фрази без ШІ");
        }

        var title = "Що пам'ятає про вас" +
                    (string.IsNullOrWhiteSpace(memory.Notes) ? " (поки нічого)" : " (" + memory.Notes.Length + " симв.)") +
                    (host.IsCompacting(npc) ? " · стискає спогади…" : string.Empty) + "##memory";
        if (ImGui.TreeNode(title))
        {
            ImGui.PushTextWrapPos(0);
            if (string.IsNullOrWhiteSpace(memory.Notes))
            {
                ImGui.TextDisabled("Персонаж ще нічого про вас не запам'ятав.");
            }
            else if (ImGui.BeginChild("##memorytext", new Vector2(0, 90), ImGuiChildFlags.Borders))
            {
                ImGui.TextWrapped(memory.Notes);
            }
            if (!string.IsNullOrWhiteSpace(memory.Notes))
            {
                ImGui.EndChild();
            }
            ImGui.PopTextWrapPos();
            ImGui.TreePop();
        }
    }

    private static void DrawActions(GameApp app, SimulationHost host, Npc npc)
    {
        var sim = host.Simulation;
        var world = sim.World;
        var quest = world.Quests.QuestOfGiver(npc.Id);
        var player = world.Player;

        // в игровых сценариях задание выдаёт сам персонаж через ШІ, кнопки не нужны
        var social = world.Game.Social;
        if (!social && quest is not null && quest.State == QuestState.NotTaken)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(UiText.TakeQuest + "##take"))
            {
                if (world.Quests.TryTake(quest.Id))
                {
                    sim.EmitManualEvent("quest_given", npc.Id, Player.PlayerId, null,
                        world.ZoneOfTile(npc.X, npc.Y), npc.X, npc.Y,
                        new Dictionary<string, string> { ["questId"] = quest.Id });
                    app.ShowNotice("Завдання взято: " + quest.TitleUk);
                }
            }
        }

        if (!social && quest is not null && player.HasItem(quest.RequiredItem) && quest.State == QuestState.Taken)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(UiText.GiveItem + "##give"))
            {
                sim.PlayerGiveItem(quest.RequiredItem, npc);
                app.ShowNotice("Предмет передано");
            }
        }

        ImGui.SameLine();
        ImGui.Checkbox(UiText.ShowTech + "##tech", ref host.ShowTechDetails);
        if (host.ShowTechDetails && host.Dialog?.LastTrace is { } trace)
        {
            ImGui.SetItemTooltip("Токени: " + trace.PromptTokens + " -> " + trace.CompletionTokens + " · " + trace.LatencyMs +
                                 " мс · кеш: " + (trace.FromCache ? "так" : "ні") + "\nДія: " + trace.Action + " · " + trace.Verdict +
                                 (string.IsNullOrEmpty(trace.RejectReason) ? string.Empty : " · " + trace.RejectReason));
            ImGui.SameLine();
            ImGui.TextDisabled(trace.Action + " · " + trace.Verdict + " · " + trace.LatencyMs + " мс");
        }
    }

    private static int _coins = 1;
    private static int _purpose;
    private static int _tradeMine;
    private static int _tradeTheirs;

    private static readonly string[] Purposes = ["просто так", "за інформацію", "щоб нікому не казали", "у борг"];

    private static readonly string[] PurposeSuffix = ["", " за інформацію", " і просить нікому нічого не казати", " у борг"];

    /// <summary>NPC потребовал плату: заплатить или отказаться.</summary>
    private static void DrawDemand(SimulationHost host, Npc npc, bool pending)
    {
        var world = host.Simulation.World;
        if (!world.Game.Demands.TryGetValue(npc.Id, out var demand))
        {
            return;
        }
        ImGui.TextColored(HintColor, npc.Name + " хоче " + demand.Coins + " мон." +
                                     (demand.Item is null ? string.Empty : " за «" + world.ItemName(demand.Item) + "»") +
                                     " · у вас " + world.Player.Coins);
        ImGui.SameLine();
        ImGui.BeginDisabled(pending || world.Player.Coins < demand.Coins);
        if (ImGui.SmallButton("Заплатити##pay"))
        {
            host.DeedPayDemand();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(pending);
        if (ImGui.SmallButton("Відмовитись##refusepay"))
        {
            host.DeedRefuseDemand();
        }
        ImGui.EndDisabled();
    }

    /// <summary>
    /// Поступки игрока: настоящие события в мире, а не слова. Текст из поля ввода идёт вместе с поступком.
    /// </summary>
    private static void DrawDeeds(SimulationHost host, Npc npc, bool pending)
    {
        var world = host.Simulation.World;
        var player = world.Player;
        var dialog = host.Dialog!;
        ImGui.BeginDisabled(pending);

        ImGui.BeginDisabled(player.Coins <= 0);
        if (ImGui.SmallButton("Дати монети##deedcoins"))
        {
            _coins = Math.Clamp(_coins, 1, Math.Max(1, player.Coins));
            ImGui.OpenPopup("##coinspopup");
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(player.Inventory.Count == 0);
        if (ImGui.SmallButton("Подарувати##deedgift"))
        {
            ImGui.OpenPopup("##giftpopup");
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Обмін##deedtrade"))
        {
            _tradeMine = 0;
            _tradeTheirs = 0;
            ImGui.OpenPopup("##tradepopup");
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.SmallButton("Потиснути руку##deedhand"))
        {
            host.DeedGesture("тисне вам руку", dialog.Input);
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Пригрозити##deedthreat"))
        {
            host.DeedThreaten(dialog.Input);
        }
        if (world.Game.Combat)
        {
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.55f, 0.20f, 0.18f, 1f));
            if (ImGui.SmallButton("Вдарити##deedhit"))
            {
                host.DeedHit(dialog.Input);
            }
            ImGui.PopStyleColor();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.SmallButton("Піти##deedleave"))
        {
            host.CloseDialog();
        }

        if (ImGui.BeginPopup("##coinspopup"))
        {
            ImGui.TextUnformatted("Скільки дати " + npc.Name + "? У вас " + player.Coins + ".");
            ImGui.SetNextItemWidth(220);
            ImGui.SliderInt("##coinsslider", ref _coins, 1, Math.Max(1, Math.Min(player.Coins, 30)));
            ImGui.SetNextItemWidth(220);
            ImGui.Combo("##coinspurpose", ref _purpose, Purposes, Purposes.Length);
            ImGui.TextDisabled("Текст з поля вводу піде разом із монетами.");
            if (ImGui.Button("Дати", new Vector2(100, 0)))
            {
                host.DeedGiveCoins(_coins, PurposeSuffix[Math.Clamp(_purpose, 0, PurposeSuffix.Length - 1)], dialog.Input);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(UiText.Cancel + "##coinscancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopup("##giftpopup"))
        {
            ImGui.TextUnformatted("Що подарувати " + npc.Name + "?");
            foreach (var item in player.Inventory.Distinct(StringComparer.Ordinal).ToList())
            {
                if (ImGui.Selectable(world.ItemName(item) + "##gift_" + item))
                {
                    host.DeedGiveItem(item, dialog.Input);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.EndPopup();
        }

        if (ImGui.BeginPopup("##tradepopup"))
        {
            var mine = player.Inventory.Distinct(StringComparer.Ordinal).ToList();
            var theirs = npc.Inventory.Distinct(StringComparer.Ordinal).ToList();
            var mineNames = mine.Select(world.ItemName).ToArray();
            var theirNames = new[] { "що-небудь" }.Concat(theirs.Select(world.ItemName)).ToArray();
            ImGui.TextUnformatted("Обмін з " + npc.Name);
            if (mine.Count == 0)
            {
                ImGui.TextDisabled("У вас нічого немає.");
            }
            else
            {
                _tradeMine = Math.Clamp(_tradeMine, 0, mine.Count - 1);
                _tradeTheirs = Math.Clamp(_tradeTheirs, 0, theirNames.Length - 1);
                ImGui.SetNextItemWidth(220);
                ImGui.Combo("ваше##trademine", ref _tradeMine, mineNames, mineNames.Length);
                ImGui.SetNextItemWidth(220);
                ImGui.Combo("на##tradetheirs", ref _tradeTheirs, theirNames, theirNames.Length);
                if (ImGui.Button("Запропонувати", new Vector2(140, 0)))
                {
                    host.DeedOfferTrade(mine[_tradeMine], _tradeTheirs == 0 ? null : theirs[_tradeTheirs - 1], dialog.Input);
                    ImGui.CloseCurrentPopup();
                }
            }
            ImGui.SameLine();
            if (ImGui.Button(UiText.Cancel + "##tradecancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private static Vector4 TrustColor(double trust) =>
        trust >= 0.55 ? GoodColor : trust >= 0.35 ? HintColor : BadColor;

    private static void DrawInventory(GameApp app, SimulationHost host)
    {
        var width = Raylib.GetScreenWidth();
        var player = host.Simulation.World.Player;
        var areaWidth = width - (app.ShowPanel ? 480 : 0);
        ImGui.SetNextWindowPos(new Vector2(Math.Max(8, areaWidth - 270), 60), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(250, 0), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.07f, 0.08f, 0.10f, 1f));
        if (ImGui.Begin("Інвентар", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Монети: " + player.Coins);
            if (player.Inventory.Count == 0)
            {
                ImGui.TextDisabled(UiText.Empty);
            }
            for (var i = 0; i < player.Inventory.Count; i++)
            {
                var itemId = player.Inventory[i];
                var name = host.Simulation.World.ItemName(itemId);
                if (ImGui.Selectable(name + "##inv" + i, player.SelectedIndex == i))
                {
                    player.SelectedIndex = i;
                }
            }
            var world = host.Simulation.World;
            var selected = player.SelectedItem;
            if (selected is not null)
            {
                ImGui.Separator();
                if (world.Game.Life && world.Items.TryGetValue(selected, out var def) && def.Food > 0 &&
                    ImGui.Button("З'їсти", new Vector2(110, 24)))
                {
                    var name = world.ItemName(selected);
                    Svidok.Core.Scenarios.GameMechanics.PlayerEat(world, selected);
                    host.ShowNotice("З'їли: " + name + ". Голод: " + (int)player.Hunger + "%.");
                }
                if (ImGui.Button("Викинути", new Vector2(110, 24)))
                {
                    var name = world.ItemName(selected);
                    Svidok.Core.Scenarios.GameMechanics.PlayerDrop(world, selected);
                    host.ShowNotice("Викинули: " + name);
                }
                if (world.Game.Social)
                {
                    ImGui.SameLine();
                    if (ImGui.Button("Підкинути тут", new Vector2(110, 24)))
                    {
                        var name = world.ItemName(selected);
                        Svidok.Core.Scenarios.GameMechanics.PlayerPlant(world, selected);
                        host.ShowNotice("Підкинули: " + name + ". Хтось знайде — і піде поголос.");
                    }
                    ImGui.SetItemTooltip("Знайде той, хто пройде поруч. У чужій хаті це виглядатиме як доказ проти господаря.");
                }
            }
            ImGui.Separator();
            ImGui.TextDisabled(world.Game.Life
                ? "F — підняти/покласти, G — віддати, Q — вкрасти, Z — з'їсти, J — працювати, X — вдарити"
                : world.Game.Combat
                    ? "F — підняти/покласти, G — віддати, Q — вкрасти, X — вдарити"
                    : "F — підняти/покласти, G — віддати, Q — вкрасти");
            if (ImGui.Button(UiText.Close, new Vector2(100, 24)))
            {
                host.ShowInventory = false;
            }
        }
        ImGui.End();
        ImGui.PopStyleColor();
    }
}
