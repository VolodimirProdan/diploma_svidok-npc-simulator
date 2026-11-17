using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Events;
using Svidok.Core.Scenarios;
using Svidok.Llm;
using Xunit;
using Xunit.Abstractions;

namespace Svidok.Tests;

public sealed class SpreadTests
{
    private readonly ITestOutputHelper _output;

    public SpreadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Rumor_Reaches_Most_Of_The_Village()
    {
        var scenario = ScenarioLoader.FindById("s1");
        Assert.NotNull(scenario);

        var config = AppConfig.LoadDefault();
        var counter = new TokenCounter();
        var runner = new ScenarioRunner(config, null,
            (simulation, variant) => LlmBootstrap.Create(simulation, variant, config.Llm, counter, dryRun: false));

        var root = Path.Combine(Path.GetTempPath(), "svidok_spread_" + Guid.NewGuid().ToString("N"));
        try
        {
            var outcome = await runner.RunAsync(scenario!, new BatchOptions
            {
                Variants = new[] { Variant.V0 },
                Seeds = new[] { 1, 2, 3 },
                ParameterSets = new[] { new RunParameters() }
            }, root, CancellationToken.None);

            foreach (var run in outcome.Runs)
            {
                _output.WriteLine(run.Scenario + " seed " + run.Seed + " events=" + run.Events);
            }
            foreach (var summary in outcome.S1Summary)
            {
                _output.WriteLine("seed " + summary.Seed + " t50=" + summary.T50 + " meanC14=" + summary.MeanC14 +
                                  " distorted=" + summary.DistortedShare);
            }

            var maxCoverage = outcome.S1Coverage.GroupBy(r => r.Seed)
                .Select(g => g.Max(r => r.Coverage))
                .DefaultIfEmpty(0)
                .Max();

            _output.WriteLine("max coverage = " + maxCoverage);
            Assert.True(maxCoverage > 0.0, "Слух не поширився взагалі");
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
