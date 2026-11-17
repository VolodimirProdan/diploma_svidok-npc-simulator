using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.WorldModel;

namespace Svidok.Game;

/// <summary>
/// Окно «Плітки»: что в селе знают и говорят об игроке, кто узнал сам, а кто с чужих слов,
/// и что ему приписывают по искажённым слухам.
/// </summary>
public static class GossipPanel
{
    private const int MaxRows = 40;

    private static readonly HashSet<string> Skipped = new(StringComparer.Ordinal)
    {
        "arrive", "eat", "work", "greet", "buy"
    };

    private static readonly Vector4 TitleColor = new(0.95f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 SeenColor = new(0.45f, 0.85f, 0.50f, 1f);
    private static readonly Vector4 RumorColor = new(0.95f, 0.75f, 0.40f, 1f);
    private static readonly Vector4 FalseColor = new(0.95f, 0.45f, 0.40f, 1f);

    public static void Draw(SimulationHost host, ref bool open)
    {
        if (!open)
        {
            return;
        }
        var world = host.Simulation.World;
        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        ImGui.SetNextWindowPos(new Vector2(width / 2f - 300, 70), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(600, Math.Min(520, height - 140)), ImGuiCond.Appearing);
        if (ImGui.Begin("Плітки##gossip", ref open, ImGuiWindowFlags.NoCollapse))
        {
            if (ImGui.BeginTabBar("##gossiptabs"))
            {
                if (ImGui.BeginTabItem("Про вас"))
                {
                    DrawAboutPlayer(world);
                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("Репутація"))
                {
                    DrawReputation(world);
                    ImGui.EndTabItem();
                }
                ImGui.EndTabBar();
            }
        }
        ImGui.End();
    }

    /// <summary>Как село в среднем относится к каждому: видно, сработали ли плітки и подставы.</summary>
    private static void DrawReputation(World world)
    {
        ImGui.TextDisabled("Скільки в середньому довіряють кожному інші жителі.");
        var rows = new List<(string Name, double Trust, bool IsPlayer, bool IsHeadman)>
        {
            ("Ви", Svidok.Core.Scenarios.StoryEngine.AverageTrust(world, Player.PlayerId), true,
                world.Game.HeadmanId == Player.PlayerId)
        };
        foreach (var npc in world.ActiveNpcs())
        {
            rows.Add((npc.Name, Svidok.Core.Scenarios.StoryEngine.AverageTrust(world, npc.Id), false,
                string.Equals(world.Game.HeadmanId, npc.Id, StringComparison.Ordinal)));
        }
        foreach (var row in rows.OrderByDescending(r => r.Trust))
        {
            var label = row.Name + (row.IsHeadman ? " (староста)" : string.Empty);
            ImGui.TextColored(row.IsPlayer ? TitleColor : new Vector4(0.85f, 0.85f, 0.88f, 1f), label);
            ImGui.SameLine(220);
            var color = row.Trust >= 0.6 ? SeenColor : row.Trust >= 0.4 ? RumorColor : FalseColor;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
            ImGui.ProgressBar((float)Math.Clamp(row.Trust, 0, 1), new Vector2(260, 14), ((int)Math.Round(row.Trust * 100)) + "%");
            ImGui.PopStyleColor();
        }
    }

    private static void DrawAboutPlayer(World world)
    {
        {
            var rows = CollectAboutPlayer(world);
            var blamed = CollectBlamed(world);

            if (rows.Count == 0 && blamed.Count == 0)
            {
                ImGui.TextDisabled("Поки про вас нічого не говорять.");
            }

            foreach (var row in rows)
            {
                DrawRow(world, row);
            }

            if (blamed.Count > 0)
            {
                ImGui.Separator();
                ImGui.TextColored(FalseColor, "Вам приписують те, чого ви не робили");
                foreach (var (npc, entry, truth) in blamed)
                {
                    ImGui.BulletText(npc.Name + " думає, що " + Headline(world, truth, asBlame: true) +
                                     " (" + SourceText(world, npc, entry) + ")");
                }
            }
        }
    }

    private sealed record GossipRow(WorldEvent Event, List<(Npc Npc, KnowledgeEntry Entry)> Knowers);

    private static List<GossipRow> CollectAboutPlayer(World world)
    {
        var rows = new List<GossipRow>();
        var events = world.Log.Events;
        for (var i = events.Count - 1; i >= 0 && rows.Count < MaxRows; i--)
        {
            var ev = events[i];
            if (Skipped.Contains(ev.Type))
            {
                continue;
            }
            var aboutPlayer = string.Equals(ev.ActorId, Player.PlayerId, StringComparison.Ordinal) ||
                              string.Equals(ev.TargetId, Player.PlayerId, StringComparison.Ordinal);
            if (!aboutPlayer)
            {
                continue;
            }
            var knowers = new List<(Npc, KnowledgeEntry)>();
            foreach (var npc in world.ActiveNpcs())
            {
                if (npc.Knowledge.ByEventId(ev.Id) is { } entry)
                {
                    knowers.Add((npc, entry));
                }
            }
            // о событии, которое знает только сам участник разговора, ещё никто не судачит
            if (knowers.Count == 0)
            {
                continue;
            }
            rows.Add(new GossipRow(ev, knowers));
        }
        return rows;
    }

    /// <summary>Слух исказился: в нём виноват игрок, хотя на самом деле был кто-то другой.</summary>
    private static List<(Npc Npc, KnowledgeEntry Entry, WorldEvent Truth)> CollectBlamed(World world)
    {
        var result = new List<(Npc, KnowledgeEntry, WorldEvent)>();
        foreach (var npc in world.ActiveNpcs())
        {
            foreach (var entry in npc.Knowledge.Entries)
            {
                if (entry.EventId is not int id || !string.Equals(entry.ActorId, Player.PlayerId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (world.Log.ById(id) is not { } truth ||
                    string.Equals(truth.ActorId, Player.PlayerId, StringComparison.Ordinal))
                {
                    continue;
                }
                result.Add((npc, entry, truth));
            }
        }
        return result;
    }

    private static void DrawRow(World world, GossipRow row)
    {
        var ev = row.Event;
        var seen = row.Knowers.Count(k => Sources.IsFirstHand(k.Entry.Source));
        var heard = row.Knowers.Count - seen;

        ImGui.PushID(ev.Id);
        ImGui.TextColored(TitleColor, world.TimeOf(ev.Tick).Split(", ")[^1]);
        ImGui.SameLine();
        ImGui.TextWrapped(Headline(world, ev, asBlame: false));

        var summary = "Знають: " + row.Knowers.Count + " · бачили самі: " + seen + " · з чужих слів: " + heard;
        if (ImGui.TreeNode(summary + "##who"))
        {
            foreach (var (npc, entry) in row.Knowers.OrderBy(k => Sources.IsFirstHand(k.Entry.Source) ? 0 : 1))
            {
                var color = Sources.IsFirstHand(entry.Source) ? SeenColor : entry.IsDistorted ? FalseColor : RumorColor;
                ImGui.TextColored(color, npc.Name);
                ImGui.SameLine();
                ImGui.TextDisabled("— " + SourceText(world, npc, entry));
            }
            ImGui.TreePop();
        }
        ImGui.Spacing();
        ImGui.PopID();
    }

    private static string SourceText(World world, Npc npc, KnowledgeEntry entry)
    {
        var certainty = (int)Math.Round(entry.C * 100) + "% певності";
        if (Sources.IsFirstHand(entry.Source))
        {
            return Sources.NameUk(entry.Source) + ", " + certainty;
        }
        var teller = entry.Chain.LastOrDefault(id => !string.Equals(id, npc.Id, StringComparison.Ordinal));
        var from = teller is null ? Sources.NameUk(entry.Source) : "чув від " + world.NpcName(teller);
        return from + ", " + certainty + (entry.IsDistorted ? ", вже перекручено" : string.Empty);
    }

    /// <summary>Коротко, по-людски: что произошло.</summary>
    private static string Headline(World world, WorldEvent ev, bool asBlame)
    {
        var actor = ev.ActorId is null ? "хтось" : world.NpcName(ev.ActorId);
        var target = ev.TargetId is null ? string.Empty : world.NpcName(ev.TargetId);
        var item = ev.ItemId is null ? string.Empty : "«" + world.ItemName(ev.ItemId) + "»";
        var byPlayer = string.Equals(ev.ActorId, Player.PlayerId, StringComparison.Ordinal);
        var subject = asBlame || byPlayer ? "ви" : actor;

        if (ev.Type == "talk" && ev.Get("text") is { } text)
        {
            return text;
        }

        var phrase = ev.Type switch
        {
            "theft" => subject + " вкрали " + item + (target.Length > 0 ? " у " + target : string.Empty),
            "attack" => byPlayer || asBlame ? "ви напали на " + target : actor + " напав на вас",
            "knockout" => byPlayer || asBlame ? "ви побили " + target + " до непритомності" : "вас побили до непритомності",
            "threat" => "ви погрожували " + target,
            "give_coins" => "ви дали монети " + target,
            "give_item" => byPlayer ? "ви подарували " + item + " " + target : actor + " дав вам " + item,
            "trade" => "ви мінялися з " + (byPlayer ? target : actor),
            "arrest" => actor + " кинув вас в обезьянник",
            "banish" => actor + " вигнав вас із селища",
            "fine" => actor + " оштрафував вас",
            "curfew" => "вас оштрафували за прогулянку вночі",
            "quest_given" => actor + " дав вам завдання",
            "quest_completed" => "ви виконали завдання для " + target,
            "reward_given" => actor + " заплатив вам нагороду",
            "item_found" => actor + " знайшов підкинутий " + item,
            "shout" => "ви кричали на все село",
            "search" => byPlayer ? "ви обшукали " + target
                : ev.Get("result") == "found" ? actor + " знайшов у вас " + item : actor + " обшукав вас і нічого не знайшов",
            _ => world.Schema.Get(ev.Type).NameUk + (byPlayer ? " (ви)" : " (" + actor + ")")
        };
        return byPlayer || asBlame ? phrase : Svidok.Core.Util.UkGrammar.For(world.Npc(ev.ActorId), phrase);
    }
}
