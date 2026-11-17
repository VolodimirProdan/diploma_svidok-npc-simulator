using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Llm;
using Svidok.Core.Scenarios;

namespace Svidok.Llm;

/// <summary>Подключение клиента модели к симуляции: решатель, сборка промптов, счётчик токенов.</summary>
public static class LlmBootstrap
{
    public static IDecisionMaker? Create(
        Simulation simulation,
        Variant variant,
        LlmConfig llmConfig,
        TokenCounter counter,
        bool dryRun = false)
    {
        simulation.TextTokenCounter = counter.Estimate;
        simulation.TokenCalibrationObserver = counter.Observe;

        // Промпты нужны и для V0: S4 измеряет объём промпта V4 для каждого NPC.
        var measurementVariant = variant == Variant.V0 ? Variant.V4 : variant;
        var builder = PromptBuilder.Load(measurementVariant, simulation.Catalog, simulation.World.Schema, simulation.World.Config);
        builder.Counter = counter.Count;
        builder.MaxPromptTokensV1 = llmConfig.MaxPromptTokensV1;
        builder.GameMode = simulation.World.Game.Social;
        simulation.NpcPromptBuilder = npc => builder.BuildForNpc(simulation.World, npc);

        if (!Variants.UsesLlm(variant))
        {
            return null;
        }

        ILlmClient client = dryRun
            ? new DryRunLlmClient(counter)
            : LlmClientFactory.Create(llmConfig);

        var maker = new LlmDecisionMaker(client, builder, simulation.Catalog, variant, simulation.Rules, llmConfig);
        simulation.LlmMaker = maker;
        simulation.PromptFactory = maker.Prepare;
        return maker;
    }

    /// <summary>
    /// Отдельный решатель для замеров S4: не становится решателем мира и не влияет на его поведение.
    /// </summary>
    public static IDecisionMaker CreateStandalone(
        Simulation simulation,
        Variant variant,
        LlmConfig llmConfig,
        TokenCounter counter,
        bool dryRun = false)
    {
        var builder = PromptBuilder.Load(variant, simulation.Catalog, simulation.World.Schema, simulation.World.Config);
        builder.Counter = counter.Count;
        builder.MaxPromptTokensV1 = llmConfig.MaxPromptTokensV1;
        builder.GameMode = simulation.World.Game.Social;

        ILlmClient client = dryRun
            ? new DryRunLlmClient(counter)
            : LlmClientFactory.Create(llmConfig);

        return new LlmDecisionMaker(client, builder, simulation.Catalog, variant, simulation.Rules, llmConfig);
    }
}
