using System.Numerics;
using ImGuiNET;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Quests;
using Svidok.Core.WorldModel;

namespace Svidok.Game;

/// <summary>
/// Панель «Що можна перевірити»: зачем сценарий, включён ли ИИ и что попробовать.
/// Её можно свернуть в одну строку (с прогрессом) и перетащить; F2 прячет совсем.
/// </summary>
public static class ObjectivesPanel
{
    private const float Width = 360f;

    private static readonly Vector4 DoneColor = new(0.45f, 0.85f, 0.50f, 1f);
    private static readonly Vector4 TodoColor = new(0.75f, 0.75f, 0.78f, 1f);
    private static readonly Vector4 WarnColor = new(0.95f, 0.80f, 0.35f, 1f);
    private static readonly Vector4 AiColor = new(0.45f, 0.80f, 0.95f, 1f);

    private sealed record Goal(bool Done, string Text, bool IsTip = false);

    public static void Draw(SimulationHost host, ref bool open)
    {
        if (!open)
        {
            return;
        }

        var goals = host.PlayerMode ? PlayerGoals(host) : new List<Goal>();
        var checks = goals.Where(g => !g.IsTip).ToList();
        var title = UiText.GoalsTitle + (checks.Count > 0
            ? " · " + checks.Count(g => g.Done) + "/" + checks.Count
            : string.Empty) + "###goals";

        ImGui.SetNextWindowPos(new Vector2(8, 44), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(Width, 0), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.88f);
        // ### — одно и то же окно при смене прогресса в заголовке, и оно не перехватывает фокус у поля ввода
        if (ImGui.Begin(title, ref open, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoFocusOnAppearing))
        {
            ImGui.PushTextWrapPos(Width - 16);

            var scenario = host.Scenario;
            ImGui.TextWrapped(UiText.ScenarioPurpose(scenario.Id, scenario.DescriptionUk));

            var variant = host.Simulation.Variant;
            if (variant == Variant.V0)
            {
                ImGui.TextColored(WarnColor, UiText.V0Warning);
            }
            else
            {
                ImGui.TextColored(AiColor, "ШІ: " + UiText.VariantLabel(variant.ToString()));
            }

            ImGui.Separator();
            if (host.PlayerMode)
            {
                ImGui.TextUnformatted("Що спробувати:");
                foreach (var goal in goals)
                {
                    if (goal.IsTip)
                    {
                        Bullet(goal.Text);
                    }
                    else
                    {
                        Check(goal.Done, goal.Text);
                    }
                }
            }
            else
            {
                ImGui.TextUnformatted("На що дивитись:");
                foreach (var tip in UiText.ObserverTips(scenario.Id))
                {
                    Bullet(tip);
                }
            }

            ImGui.Separator();
            ImGui.TextDisabled("Згорнути — трикутник угорі · F2 — сховати · F3 — сховати весь інтерфейс");
            ImGui.PopTextWrapPos();
        }
        ImGui.End();
    }

    private static List<Goal> PlayerGoals(SimulationHost host)
    {
        var world = host.Simulation.World;
        var player = world.Player;
        var goals = new List<Goal>
        {
            new(host.DialogsSent > 0, "Клікніть на персонажа поруч (або E) і поговоріть. Він пам'ятатиме вас і наступного разу.")
        };

        var quest = world.Quests.All.Values.FirstOrDefault();
        if (quest is not null)
        {
            var giver = world.Npc(quest.GiverId);
            var giverName = giver?.Name ?? quest.GiverId;
            var giverPlace = giver is null ? string.Empty : " (" + world.ZoneName(giver.WorkZone) + ")";
            var itemName = world.ItemName(quest.RequiredItem);

            goals.Add(new Goal(quest.State != QuestState.NotTaken, world.Game.Social
                ? "Попросіть у " + giverName + giverPlace + " роботу. Якщо сподобаєтесь — дасть завдання «" + quest.TitleUk + "»."
                : "Візьміть завдання у " + giverName + giverPlace + ": «" + quest.TitleUk + "»."));
            goals.Add(new Goal(host.TriedToDeceive,
                "Спробуйте вмовити " + giverName + " заплатити без предмета. Чи повірить?"));
            goals.Add(new Goal(player.HasItem(quest.RequiredItem) || quest.IsCompletedOrRewarded,
                "Знайдіть «" + itemName + "» на складі: станьте на предмет і натисніть F."));
            goals.Add(new Goal(quest.State == QuestState.Rewarded, world.Game.Social
                ? "Подаруйте «" + itemName + "» (кнопка в розмові) і попросіть нагороду."
                : "Віддайте «" + itemName + "» і попросіть нагороду."));
        }

        if (string.Equals(host.Scenario.Id, "s6", StringComparison.OrdinalIgnoreCase))
        {
            goals.Add(new Goal(host.PlayerStole,
                "Вкрадіть срібний кубок у Марти на ринку (Q поруч). Цікавіше, коли хтось бачить."));
            goals.Add(new Goal(host.DialogsAfterTheft >= 2,
                "Розпитайте людей про кубок: очевидці знають, інші — лише чутки або нічого. R — хто знає."));
            goals.Add(new Goal(false,
                "Нагрубіть комусь, а потім поговоріть з його сусідом: чи дійшла до нього чутка?", true));
            goals.Add(new Goal(false,
                "Опівдні прийде мандрівник Захар. Спитайте про нього тих, хто бачив, і тих, хто ні.", true));
        }

        if (world.Game.Social)
        {
            goals.Add(new Goal(ByPlayer(world, "give_coins"),
                "Дайте комусь монети (кнопка «Дати монети») і попросіть нікому не казати. Чи дотримає слова?"));
            goals.Add(new Goal(world.Quests.All.Values.Any(q => q.AutoReward && q.State == QuestState.Rewarded),
                "Допоможіть комусь: візьміть доручення на дошці (O) — знайти, віднести чи допомогти з роботою. Платять одразу."));
            goals.Add(new Goal(ByPlayer(world, "trade"),
                "Обміняйтесь предметом з кимось (кнопка «Обмін»)."));
            goals.Add(new Goal(world.Game.ItemsFound > 0,
                "Підкиньте вкрадене в чужу хату вночі (інвентар, «Підкинути тут»). Кого арештують?"));
        }
        if (world.Game.Combat)
        {
            goals.Add(new Goal(ByPlayer(world, "attack"),
                "Побийтеся (X поруч або «Вдарити»). Подивіться, хто покличе варту.", true));
        }
        if (world.Game.Life)
        {
            goals.Add(new Goal(ByPlayer(world, "work"),
                "Заробіть монети: підійдіть до кузні, ставка, поля, складу чи млина й натисніть J (з 7:00 до 19:00)."));
            goals.Add(new Goal(ByPlayer(world, "eat"),
                "Поїжте (Z), коли голод зросте. Від сильного голоду падає здоров'я."));
            goals.Add(new Goal(false,
                "З 22:00 до 5:00 сидіть удома (" + world.ZoneName(player.HomeZone) + "): варта штрафує за прогулянки.", true));
        }

        if (host.Simulation.Variant != Variant.V1)
        {
            goals.Add(new Goal(false,
                "Для порівняння зіграйте з V1: там ШІ бачить усе і «знає» навіть те, чого персонаж не бачив.", true));
        }
        return goals;
    }

    private static bool ByPlayer(World world, string type)
    {
        foreach (var ev in world.Log.OfType(type))
        {
            if (string.Equals(ev.ActorId, Player.PlayerId, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static void Check(bool done, string text)
    {
        ImGui.TextColored(done ? DoneColor : TodoColor, done ? "[x]" : "[  ]");
        ImGui.SameLine();
        if (done)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, DoneColor);
            ImGui.TextWrapped(text);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextWrapped(text);
        }
    }

    private static void Bullet(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }
}
