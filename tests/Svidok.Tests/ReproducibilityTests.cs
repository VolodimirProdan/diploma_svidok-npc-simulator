using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Scenarios;
using Svidok.Llm;
using Xunit;

namespace Svidok.Tests;

public sealed class ReproducibilityTests
{
    [Fact]
    public async Task Two_Runs_With_Same_Seed_Produce_Same_Coverage()
    {
        var scenario = ScenarioLoader.FindById("s1");
        Assert.NotNull(scenario);

        var config = AppConfig.LoadDefault();
        var counter = new TokenCounter();
        var runner = new ScenarioRunner(config, null,
            (simulation, variant) => LlmBootstrap.Create(simulation, variant, config.Llm, counter, dryRun: false));

        var root = Path.Combine(Path.GetTempPath(), "svidok_repro_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var options = new BatchOptions
            {
                Variants = new[] { Variant.V0 },
                Seeds = new[] { 5 },
                ParameterSets = new[] { new RunParameters() }
            };

            var first = await runner.RunAsync(scenario!, options, Path.Combine(root, "a"), CancellationToken.None);
            var second = await runner.RunAsync(scenario!, options, Path.Combine(root, "b"), CancellationToken.None);

            var firstCsv = File.ReadAllText(Path.Combine(root, "a", "s1_coverage.csv"));
            var secondCsv = File.ReadAllText(Path.Combine(root, "b", "s1_coverage.csv"));
            Assert.Equal(firstCsv, secondCsv);
            Assert.Equal(first.Runs[0].Events, second.Runs[0].Events);
            Assert.Equal(first.Runs[0].LogBytes, second.Runs[0].LogBytes);
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
