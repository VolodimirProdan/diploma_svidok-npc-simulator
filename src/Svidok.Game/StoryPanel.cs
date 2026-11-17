using System.Numerics;
using ImGuiNET;
using Raylib_cs;
using Svidok.Core.Scenarios;

namespace Svidok.Game;

/// <summary>
/// Панель «Сюжет»: текущий этап, что нужно для каждого пути дальше (с прогрессом) и кнопки решений игрока.
/// </summary>
public static class StoryPanel
{
    private static readonly Vector4 TitleColor = new(0.95f, 0.85f, 0.55f, 1f);
    private static readonly Vector4 DoneColor = new(0.45f, 0.85f, 0.50f, 1f);
    private static readonly Vector4 TodoColor = new(0.80f, 0.80f, 0.84f, 1f);

    public static void Draw(SimulationHost host, ref bool open)
    {
        if (!open)
        {
            return;
        }
        var world = host.Simulation.World;
        var width = Raylib.GetScreenWidth();
        var height = Raylib.GetScreenHeight();
        ImGui.SetNextWindowPos(new Vector2(width / 2f - 320, 70), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(new Vector2(640, Math.Min(580, height - 140)), ImGuiCond.Appearing);
        if (ImGui.Begin("Сюжет##story", ref open, ImGuiWindowFlags.NoCollapse))
        {
            if (world.Game.Stories.Count == 0)
            {
                ImGui.TextDisabled("У цьому сценарії немає сюжету.");
            }
            foreach (var story in world.Game.Stories)
            {
                DrawStory(host, world, story);
            }
        }
        ImGui.End();
    }

    private static void DrawStory(SimulationHost host, Svidok.Core.WorldModel.World world, StoryState story)
    {
        ImGui.PushID(story.Definition.Id);
        ImGui.TextColored(TitleColor, story.Definition.TitleUk);
        if (!story.Started)
        {
            ImGui.TextDisabled("Ще не почався. Чекайте новин у селі.");
            ImGui.PopID();
            return;
        }
        var stage = story.Stage;
        if (stage is null)
        {
            ImGui.PopID();
            return;
        }
        ImGui.TextUnformatted(stage.TitleUk);
        ImGui.TextWrapped(stage.DescriptionUk);
        if (story.Result is not null && stage.Final)
        {
            ImGui.TextColored(DoneColor, story.Result);
        }

        foreach (var exit in stage.Exits)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(TitleColor, exit.LabelUk);
            foreach (var condition in exit.When)
            {
                var progress = StoryEngine.Evaluate(world, story, condition);
                if (progress.Button is not null && progress.Flag is not null && !progress.Done)
                {
                    if (ImGui.SmallButton(progress.Button + "##" + progress.Flag))
                    {
                        story.Flags.Add(progress.Flag);
                        host.ShowNotice(progress.Button + ".");
                    }
                    continue;
                }
                ImGui.TextColored(progress.Done ? DoneColor : TodoColor, (progress.Done ? "[x] " : "[  ] ") + progress.Text);
            }
        }

        if (story.Journal.Count > 0 && ImGui.TreeNode("Що вже сталося##journal"))
        {
            foreach (var line in story.Journal)
            {
                ImGui.BulletText(line);
            }
            ImGui.TreePop();
        }
        ImGui.Spacing();
        ImGui.PopID();
    }
}
