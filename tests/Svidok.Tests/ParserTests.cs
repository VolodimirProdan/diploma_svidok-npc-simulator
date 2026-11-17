using Svidok.Core.Decisions;
using Svidok.Core.Llm;
using Svidok.Llm;
using Xunit;

namespace Svidok.Tests;

public sealed class ParserTests
{
    [Fact]
    public void Markdown_Wrapper_Is_Accepted()
    {
        var catalog = ActionCatalog.LoadDefault();
        var content = "```json\n{\"action\":\"say\",\"say\":\"Добрий день.\",\"reason\":\"тест\"}\n```";

        var ok = ResponseParser.TryParse(content, catalog, out var decision, out var error);

        Assert.True(ok, error);
        Assert.Equal("say", decision.Action);
        Assert.Equal("Добрий день.", decision.Say);
    }

    [Fact]
    public void Extra_Fields_Are_Ignored()
    {
        var catalog = ActionCatalog.LoadDefault();
        var content = "{\"action\":\"ignore\",\"unknown\":42,\"say\":\"\"}";

        var ok = ResponseParser.TryParse(content, catalog, out var decision, out _);

        Assert.True(ok);
        Assert.Equal("ignore", decision.Action);
    }

    [Fact]
    public void Unknown_Action_Is_Rejected_By_Parser()
    {
        var catalog = ActionCatalog.LoadDefault();
        var ok = ResponseParser.TryParse("{\"action\":\"fly_to_moon\"}", catalog, out _, out var error);

        Assert.False(ok);
        Assert.Contains("каталог", error);
    }

    [Fact]
    public async Task Empty_Answer_Retries_Then_Falls_Back_To_V0()
    {
        var scenario = new Svidok.Core.Scenarios.ScenarioDefinition
        {
            Id = "unit",
            DurationTicks = 10,
            Start = new Svidok.Core.Scenarios.StartSpec { Time = "12:00" }
        };
        var config = Svidok.Core.Config.AppConfig.LoadDefault();
        using var simulation = new Svidok.Core.Scenarios.Simulation(scenario, config, Variant.V4, 1);
        var npc = simulation.World.Npcs[0];
        var catalog = ActionCatalog.LoadDefault();
        var builder = PromptBuilder.Load(Variant.V4, catalog, simulation.World.Schema, simulation.World.Config);
        var client = new EmptyLlmClient();
        var maker = new LlmDecisionMaker(client, builder, catalog, Variant.V4, simulation.Rules, config.Llm);

        var ctx = new DecisionContext(simulation.World, npc, Trigger.Manual, null, Array.Empty<DialogLine>());
        var result = await maker.DecideAsync(ctx, CancellationToken.None);

        Assert.Equal(2, client.Calls);
        Assert.True(result.ParseError);
        Assert.NotNull(result.Executed);
    }

    private sealed class EmptyLlmClient : ILlmClient
    {
        public int Calls { get; private set; }

        public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new LlmResult("   ", 10, 0, 10, 0, 5, false, null));
        }
    }
}
