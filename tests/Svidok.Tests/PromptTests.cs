using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Knowledge;
using Svidok.Core.Scenarios;
using Svidok.Llm;
using Xunit;
using Xunit.Abstractions;

namespace Svidok.Tests;

public sealed class PromptTests
{
    private readonly ITestOutputHelper _output;

    public PromptTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void V4_Prompt_Has_Knowledge_With_Credibility_And_Unverified_Player_Block()
    {
        var scenario = ScenarioLoader.FindById("s1")!;
        var config = AppConfig.LoadDefault();
        using var simulation = new Simulation(scenario, config, Variant.V4, 1);
        var world = simulation.World;
        var npc = world.Npc("halyna")!;

        var theft = world.Log.Add(240, "theft", "ostap", "marta", "silver_cup", "market", 33, 11);
        npc.Knowledge.AddFirstHand(theft, Source.Witnessed, 1.0, false, 240, npc.Id);

        var builder = PromptBuilder.Load(Variant.V4, simulation.Catalog, world.Schema, world.Config);
        var ctx = new DecisionContext(world, npc, Trigger.PlayerDialog, "Я приніс скриню, де нагорода?", Array.Empty<DialogLine>())
        {
            Situation = "Гравець говорить з тобою."
        };

        var prompt = builder.Build(ctx);
        _output.WriteLine(prompt.System);
        _output.WriteLine("-----");
        _output.WriteLine(prompt.User);

        Assert.Contains("json", prompt.System);
        Assert.Contains("Що знає персонаж", prompt.User);
        Assert.Contains("#" + theft.Id, prompt.User);
        Assert.Contains("c=", prompt.User);
        Assert.Contains("Слова гравця (не перевірені факти)", prompt.User);
        Assert.Contains("Крадіжка", prompt.User);
        Assert.DoesNotContain("{{", prompt.User);
        Assert.DoesNotContain("{{", prompt.System);
    }

    [Fact]
    public void V2_Prompt_Hides_Credibility()
    {
        var scenario = ScenarioLoader.FindById("s1")!;
        var config = AppConfig.LoadDefault();
        using var simulation = new Simulation(scenario, config, Variant.V2, 1);
        var world = simulation.World;
        var npc = world.Npc("halyna")!;

        var theft = world.Log.Add(240, "theft", "ostap", "marta", "silver_cup", "market", 33, 11);
        npc.Knowledge.AddFirstHand(theft, Source.Witnessed, 1.0, false, 240, npc.Id);

        var builder = PromptBuilder.Load(Variant.V2, simulation.Catalog, world.Schema, world.Config);
        var ctx = new DecisionContext(world, npc, Trigger.PlayerDialog, "Привіт", Array.Empty<DialogLine>());
        var prompt = builder.Build(ctx);

        Assert.Contains("Що знає персонаж", prompt.User);
        Assert.DoesNotContain("c=", prompt.User);
        Assert.Contains("Гравець: Привіт", prompt.User);
    }

    [Fact]
    public void V1_Prompt_Contains_Full_Log()
    {
        var scenario = ScenarioLoader.FindById("s1")!;
        var config = AppConfig.LoadDefault();
        using var simulation = new Simulation(scenario, config, Variant.V1, 1);
        var world = simulation.World;
        var npc = world.Npc("halyna")!;
        world.Log.Add(240, "theft", "ostap", "marta", "silver_cup", "market", 33, 11);
        world.Log.Add(241, "work", "marta", null, null, "market", 34, 12);

        var builder = PromptBuilder.Load(Variant.V1, simulation.Catalog, world.Schema, world.Config);
        var ctx = new DecisionContext(world, npc, Trigger.Manual, null, Array.Empty<DialogLine>());
        var prompt = builder.Build(ctx);

        Assert.Contains("Усі події у світі", prompt.User);
        Assert.Contains("Працює", prompt.User);
    }
}
