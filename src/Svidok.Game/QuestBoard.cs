using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Svidok.Core.Quests;
using Svidok.Core.Scenarios;
using Svidok.Core.WorldModel;

namespace Svidok.Game;

/// <summary>
/// «Дошка оголошень»: доручення жителів (знайти, віднести, допомогти) и ваши взятые задания с подсказкой,
/// что делать дальше. Плюс как ещё заработать.
/// </summary>
public static class QuestBoard
{
    private static readonly Vector4 TitleColor = new(0.95f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 DoneColor = new(0.45f, 0.85f, 0.50f, 1f);
    private static readonly Vector4 CoinColor = new(0.95f, 0.80f, 0.35f, 1f);

    public static void Draw(SimulationHost host, ref bool open)
    {
        if (!open)
        {
            return;
        }
        var world = host.Simulation.World;
        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        ImGui.SetNextWindowPos(new Vector2(width / 2f - 310, 70), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(620, Math.Min(560, height - 140)), ImGuiCond.Appearing);
        if (ImGui.Begin("Дошка оголошень##questboard", ref open, ImGuiWindowFlags.NoCollapse))
        {
            var quests = world.Quests.All.Values.ToList();
            var mine = quests.Where(q => q.State is QuestState.Taken or QuestState.Completed).ToList();
            var offered = quests.Where(q => q.State == QuestState.NotTaken).OrderBy(q => q.CreatedTick).ToList();

            ImGui.TextColored(TitleColor, "Ваші завдання");
            if (mine.Count == 0)
            {
                ImGui.TextDisabled("Поки немає. Візьміть щось нижче.");
            }
            foreach (var quest in mine)
            {
                DrawMine(world, quest);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(TitleColor, "Просять допомогти");
            if (offered.Count == 0)
            {
                ImGui.TextDisabled("Зараз ніхто нічого не просить. Нові доручення з'являються вдень, приблизно раз на дві години.");
            }
            foreach (var quest in offered)
            {
                DrawOffered(host, world, quest);
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(TitleColor, "Як ще заробити");
            if (host.Scenario.Jobs.Count > 0)
            {
                foreach (var job in host.Scenario.Jobs)
                {
                    ImGui.BulletText(job.TitleUk + " — " + world.ZoneName(job.Zone) + ", J, +" + job.Pay + " мон. за пів години (7:00–19:00)");
                }
            }
            ImGui.BulletText("Продати чи обміняти річ: кнопка «Обмін» у розмові або домовтеся про ціну.");
            ImGui.BulletText("Хтось вимагає плату за послугу чи інформацію — можна торгуватися.");
        }
        ImGui.End();
    }

    private static void DrawMine(World world, Quest quest)
    {
        ImGui.PushID(quest.Id);
        var giver = world.NpcName(quest.GiverId);
        var done = quest.State == QuestState.Completed;
        ImGui.TextColored(done ? DoneColor : new Vector4(1, 1, 1, 1), (done ? "[x] " : "[  ] ") + quest.TitleUk);
        ImGui.SameLine();
        ImGui.TextColored(CoinColor, quest.RewardCoins + " мон.");
        ImGui.Indent();
        ImGui.TextDisabled("Від: " + giver);
        if (done)
        {
            ImGui.TextWrapped("Зроблено. Попросіть у " + giver + " нагороду.");
        }
        else
        {
            ImGui.TextWrapped(NextStep(world, quest));
        }
        ImGui.Unindent();
        ImGui.Spacing();
        ImGui.PopID();
    }

    private static void DrawOffered(SimulationHost host, World world, Quest quest)
    {
        ImGui.PushID(quest.Id);
        ImGui.TextUnformatted(quest.TitleUk);
        ImGui.SameLine();
        ImGui.TextColored(CoinColor, quest.RewardCoins + " мон.");
        ImGui.Indent();
        ImGui.TextDisabled("Просить: " + world.NpcName(quest.GiverId));
        if (!string.IsNullOrWhiteSpace(quest.HintUk))
        {
            ImGui.TextWrapped(quest.HintUk);
        }
        if (quest.AutoReward)
        {
            if (ImGui.SmallButton("Взятися"))
            {
                if (GameMechanics.PlayerTakeQuest(world, quest.Id))
                {
                    host.ShowNotice("Взяли: " + quest.TitleUk +
                                    (quest.Kind == QuestKind.Deliver ? ". Посилка у вас в інвентарі." : "."));
                }
            }
        }
        else
        {
            ImGui.TextDisabled("Про це треба поговорити з " + world.NpcName(quest.GiverId) + ".");
        }
        ImGui.Unindent();
        ImGui.Spacing();
        ImGui.PopID();
    }

    private static string NextStep(World world, Quest quest)
    {
        var player = world.Player;
        var giver = world.NpcName(quest.GiverId);
        var item = string.IsNullOrEmpty(quest.RequiredItem) ? string.Empty : "«" + world.ItemName(quest.RequiredItem) + "»";
        return quest.Kind switch
        {
            QuestKind.Find when player.HasItem(quest.RequiredItem) => "Знайшли. Віддайте " + item + " " + giver + ": кнопка «Подарувати» в розмові.",
            QuestKind.Find => quest.HintUk,
            QuestKind.Deliver when player.HasItem(quest.RequiredItem) =>
                "Віддайте " + item + " " + world.NpcName(quest.RecipientId) + " («Подарувати» в розмові). Зазвичай буває: " +
                world.ZoneName(world.Npc(quest.RecipientId)?.WorkZone) + ".",
            QuestKind.Deliver => "Посилки " + item + " у вас уже немає. Без неї доручення не виконати.",
            QuestKind.Help => quest.HintUk,
            _ => player.HasItem(quest.RequiredItem)
                ? "У вас є " + item + ". Віддайте " + giver + " і попросіть нагороду."
                : "Потрібно знайти " + item + " і віддати " + giver + "."
        };
    }
}
