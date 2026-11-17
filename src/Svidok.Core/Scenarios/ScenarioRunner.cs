using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Metrics;
using Svidok.Core.Util;

namespace Svidok.Core.Scenarios;

/// <summary>Набор параметров сценария для перебора (S1: trust × talkativeness).</summary>
public sealed class RunParameters
{
    public double TrustMultiplier { get; init; } = 1.0;

    public double TalkativenessMultiplier { get; init; } = 1.0;

    public string ToText() =>
        "trust=" + TrustMultiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
        ";talk=" + TalkativenessMultiplier.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class BatchOptions
{
    public IReadOnlyList<Variant> Variants { get; init; } = [Variant.V0];

    public IReadOnlyList<int> Seeds { get; init; } = [1];

    public IReadOnlyList<RunParameters> ParameterSets { get; init; } = [new RunParameters()];

    public bool DryRun { get; init; }

    public bool WaitForLlm { get; init; } = true;

    public bool CollectMetrics { get; init; } = true;

    public bool LiveS4Calls { get; init; }

    public Action<string>? Progress { get; init; }

    public int? MaxRuns { get; init; }

    /// <summary>S4: реальные замеры V1/V4 в конце 1–3 суток (тратит токены, поэтому по умолчанию выключено).</summary>
    public bool S4Calls { get; init; }

    /// <summary>Фабрика отдельного решателя для замеров S4, не влияющая на поведение мира.</summary>
    public SimulationDecorator? MeasurementMaker { get; init; }
}

/// <summary>Подключение клиента модели к симуляции (реализуется в Svidok.Llm).</summary>
public delegate IDecisionMaker? SimulationDecorator(Simulation simulation, Variant variant);

/// <summary>Результаты пакетного прогона: все строки CSV раздела 15.</summary>
public sealed class BatchOutcome
{
    public List<RunRow> Runs { get; } = new();

    public List<S1CoverageRow> S1Coverage { get; } = new();

    public List<S1SummaryRow> S1Summary { get; } = new();

    public List<S2AnswerRow> S2Answers { get; } = new();

    /// <summary>Шаблон для ручной проверки 50 ответов (раздел 13.2): колонку manual заполняет человек.</summary>
    public List<S2ManualRow> S2Manual { get; } = new();

    public List<S3AttackRow> S3Attacks { get; } = new();

    public List<S3HonestRow> S3Honest { get; } = new();

    public List<S4HourRow> S4Hourly { get; } = new();

    public List<S4CallRow> S4Calls { get; } = new();

    public List<S5ReactionRow> S5Reactions { get; } = new();

    public List<S7Row> S7Rows { get; } = new();

    public List<LlmCallRow> LlmCalls { get; } = new();

    public List<CalibrationRow> Calibration { get; } = new();

    public string OutputDir { get; set; } = string.Empty;

    public double TotalCost => Runs.Sum(r => r.CostUsd);

    public int TotalLlmCalls => Runs.Sum(r => r.LlmCalls);

    public void Write(string directory)
    {
        OutputDir = directory;
        Directory.CreateDirectory(directory);

        CsvWriter.Write(Path.Combine(directory, "runs.csv"),
            ["scenario", "variant", "seed", "params", "ticks", "events", "logBytes", "llmCalls", "promptTokens",
             "cacheHitTokens", "completionTokens", "costUsd", "parseErrors", "rejections", "durationSec"],
            Runs.Select(r => new[]
            {
                r.Scenario, r.Variant, CsvWriter.Int(r.Seed), r.Params, CsvWriter.Int(r.Ticks), CsvWriter.Int(r.Events),
                CsvWriter.Int((int)r.LogBytes), CsvWriter.Int(r.LlmCalls), CsvWriter.Int(r.PromptTokens),
                CsvWriter.Int(r.CacheHitTokens), CsvWriter.Int(r.CompletionTokens), CsvWriter.Num(r.CostUsd, 6),
                CsvWriter.Int(r.ParseErrors), CsvWriter.Int(r.Rejections), CsvWriter.Num(r.DurationSec, 2)
            }));

        if (S1Coverage.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s1_coverage.csv"),
                ["scenario", "variant", "seed", "trustMult", "talkMult", "tick", "coverage", "meanC", "distorted", "wrongSuspect"],
                S1Coverage.Select(r => new[]
                {
                    r.Scenario, r.Variant, CsvWriter.Int(r.Seed), CsvWriter.Num(r.TrustMult), CsvWriter.Num(r.TalkMult),
                    CsvWriter.Int(r.Tick), CsvWriter.Num(r.Coverage), CsvWriter.Num(r.MeanC),
                    CsvWriter.Num(r.Distorted), CsvWriter.Num(r.WrongSuspect)
                }));
        }

        if (S1Summary.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s1_summary.csv"),
                ["variant", "seed", "trustMult", "talkMult", "t50", "t90", "meanC14", "meanC22",
                 "distortedShare", "wrongSuspectShare", "arrestTick", "arrestedId"],
                S1Summary.Select(r => new[]
                {
                    r.Variant, CsvWriter.Int(r.Seed), CsvWriter.Num(r.TrustMult), CsvWriter.Num(r.TalkMult),
                    r.T50?.ToString() ?? "", r.T90?.ToString() ?? "", CsvWriter.Num(r.MeanC14), CsvWriter.Num(r.MeanC22),
                    CsvWriter.Num(r.DistortedShare), CsvWriter.Num(r.WrongSuspectShare),
                    r.ArrestTick?.ToString() ?? "", r.ArrestedId
                }));
        }

        if (S2Answers.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s2_answers.csv"),
                ["variant", "seed", "big", "npcId", "knows", "question", "action", "eventRef", "say", "reason",
                 "leak", "hallucination", "recallHit", "promptTokens", "latencyMs"],
                S2Answers.Select(r => new[]
                {
                    r.Variant, CsvWriter.Int(r.Seed), CsvWriter.Bool(r.Big), r.NpcId, CsvWriter.Bool(r.Knows), r.Question,
                    r.Action, r.EventRef, r.Say, r.Reason, CsvWriter.Bool(r.Leak), CsvWriter.Bool(r.Hallucination), CsvWriter.Bool(r.RecallHit),
                    CsvWriter.Int(r.PromptTokens), CsvWriter.Int((int)r.LatencyMs)
                }));
        }

        if (S2Manual.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s2_manual.csv"),
                ["variant", "seed", "npcId", "knows", "question", "say", "leakAuto", "manual"],
                S2Manual.Select(r => new[]
                {
                    r.Variant, CsvWriter.Int(r.Seed), r.NpcId, CsvWriter.Bool(r.Knows), r.Question, r.Say,
                    CsvWriter.Bool(r.LeakAuto), r.Manual
                }));
        }

        if (S3Attacks.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s3_attacks.csv"),
                ["variant", "seed", "attackId", "category", "text", "action", "validatorVerdict", "rejectReason",
                 "rewardGiven", "say", "promptTokens", "latencyMs"],
                S3Attacks.Select(r => new[]
                {
                    r.Variant, CsvWriter.Int(r.Seed), r.AttackId, r.Category, r.Text, r.Action, r.ValidatorVerdict,
                    r.RejectReason, CsvWriter.Bool(r.RewardGiven), r.Say, CsvWriter.Int(r.PromptTokens),
                    CsvWriter.Int((int)r.LatencyMs)
                }));
        }

        if (S3Honest.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s3_honest.csv"),
                ["variant", "seed", "action", "rewardGiven", "say"],
                S3Honest.Select(r => new[]
                {
                    r.Variant, CsvWriter.Int(r.Seed), r.Action, CsvWriter.Bool(r.RewardGiven), r.Say
                }));
        }

        if (S4Hourly.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s4_hourly.csv"),
                ["seed", "hour", "events", "logBytes", "tokensFull", "tokensNpcMean", "tokensNpcMin", "tokensNpcMax", "K"],
                S4Hourly.Select(r => new[]
                {
                    CsvWriter.Int(r.Seed), CsvWriter.Int(r.Hour), CsvWriter.Int(r.Events), CsvWriter.Int((int)r.LogBytes),
                    CsvWriter.Int(r.TokensFull), CsvWriter.Int(r.TokensNpcMean), CsvWriter.Int(r.TokensNpcMin),
                    CsvWriter.Int(r.TokensNpcMax), CsvWriter.Num(r.K, 2)
                }));
        }

        if (S4Calls.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s4_calls.csv"),
                ["seed", "day", "variant", "npcId", "promptTokens", "cacheHit", "cacheMiss", "completionTokens",
                 "latencyMs", "costUsd"],
                S4Calls.Select(r => new[]
                {
                    CsvWriter.Int(r.Seed), CsvWriter.Int(r.Day), r.Variant, r.NpcId, CsvWriter.Int(r.PromptTokens),
                    CsvWriter.Int(r.CacheHit), CsvWriter.Int(r.CacheMiss), CsvWriter.Int(r.CompletionTokens),
                    CsvWriter.Int((int)r.LatencyMs), CsvWriter.Num(r.CostUsd, 6)
                }));
        }

        if (S5Reactions.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s5_reactions.csv"),
                ["variant", "waitForLlm", "seed", "npcId", "source", "c", "action", "reactionEvent", "reactionMinutes",
                 "latencyMs", "queueMs", "rejected"],
                S5Reactions.Select(r => new[]
                {
                    r.Variant, CsvWriter.Bool(r.WaitForLlm), CsvWriter.Int(r.Seed), r.NpcId, r.Source, CsvWriter.Num(r.C),
                    r.Action, r.ReactionEvent, r.ReactionMinutes?.ToString() ?? "", CsvWriter.Int((int)r.LatencyMs),
                    CsvWriter.Int((int)r.QueueMs), CsvWriter.Bool(r.Rejected)
                }));
        }

        if (S7Rows.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "s7_summary.csv"),
                ["scenario", "variant", "seed", "finalCoverage", "t50", "secretBlocked", "secretLeaked", "promiseBreaks",
                 "promiseRejected", "itemsFound", "arrested", "trueCulprit", "framed", "wrongArrest", "rightArrest",
                 "noGroundsRejected"],
                S7Rows.Select(r => new[]
                {
                    r.Scenario, r.Variant, CsvWriter.Int(r.Seed), CsvWriter.Num(r.FinalCoverage), r.T50?.ToString() ?? "",
                    CsvWriter.Int(r.SecretBlocked), CsvWriter.Int(r.SecretLeaked), CsvWriter.Int(r.PromiseBreaks),
                    CsvWriter.Int(r.PromiseRejected), CsvWriter.Int(r.ItemsFound), r.Arrested, r.TrueCulprit, r.Framed,
                    CsvWriter.Bool(r.WrongArrest), CsvWriter.Bool(r.RightArrest), CsvWriter.Int(r.ArrestsRejected)
                }));
        }

        if (LlmCalls.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "llm_calls.csv"),
                ["tick", "npcId", "variant", "trigger", "promptTokens", "cacheHit", "cacheMiss", "completionTokens",
                 "latencyMs", "costUsd", "action", "verdict", "rejectReason", "parseError", "cacheFile"],
                LlmCalls.Select(r => new[]
                {
                    CsvWriter.Int(r.Tick), r.NpcId, r.Variant, r.Trigger, CsvWriter.Int(r.PromptTokens),
                    CsvWriter.Int(r.CacheHit), CsvWriter.Int(r.CacheMiss), CsvWriter.Int(r.CompletionTokens),
                    CsvWriter.Int((int)r.LatencyMs), CsvWriter.Num(r.CostUsd, 6), r.Action, r.Verdict,
                    r.RejectReason, CsvWriter.Bool(r.ParseError), r.CacheFile
                }));
        }

        if (Calibration.Count > 0)
        {
            CsvWriter.Write(Path.Combine(directory, "calibration.csv"),
                ["requestId", "localTokens", "apiPromptTokens", "ratio"],
                Calibration.Select(r => new[]
                {
                    CsvWriter.Int(r.RequestId), CsvWriter.Int(r.LocalTokens), CsvWriter.Int(r.ApiPromptTokens),
                    CsvWriter.Num(r.Ratio, 3)
                }));
        }
    }

    /// <summary>Сводная таблица метрик по вариантам для консоли.</summary>
    public string BuildSummaryTable()
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine();
        builder.AppendLine("вариант  прогонів  запитів  токенів входу  вартість $  p50 мс  p95 мс  MKR    MSR    FRR    K");
        foreach (var group in Runs.GroupBy(r => r.Variant).OrderBy(g => g.Key, StringComparer.Ordinal))
        {
            var variant = group.Key;
            var calls = LlmCalls.Where(c => string.Equals(c.Variant, variant, StringComparison.Ordinal)).ToList();
            var latencies = calls.Select(c => (double)c.LatencyMs).Where(v => v > 0).OrderBy(v => v).ToList();
            var p50 = Percentile(latencies, 0.5);
            var p95 = Percentile(latencies, 0.95);
            var promptTokens = group.Sum(r => (long)r.PromptTokens);
            var cost = group.Sum(r => r.CostUsd);

            var mkr = S2Answers.Where(a => string.Equals(a.Variant, variant, StringComparison.Ordinal)).ToList();
            var mkrValue = mkr.Count == 0 ? "-" : (mkr.Count(a => a.Leak && !a.Knows) / (double)Math.Max(1, mkr.Count(a => !a.Knows))).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

            var attacks = S3Attacks.Where(a => string.Equals(a.Variant, variant, StringComparison.Ordinal)).ToList();
            var msr = attacks.Count == 0 ? "-" : (attacks.Count(a => a.RewardGiven) / (double)attacks.Count).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

            var honest = S3Honest.Where(a => string.Equals(a.Variant, variant, StringComparison.Ordinal)).ToList();
            var frr = honest.Count == 0 ? "-" : (honest.Count(a => !a.RewardGiven) / (double)honest.Count).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);

            var k = S4Hourly.Count == 0 ? "-" : S4Hourly.Average(r => r.K).ToString("0", System.Globalization.CultureInfo.InvariantCulture);

            builder.AppendLine(string.Join("  ",
                variant.PadRight(7),
                group.Count().ToString().PadRight(8),
                group.Sum(r => r.LlmCalls).ToString().PadRight(8),
                promptTokens.ToString().PadRight(13),
                cost.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture).PadRight(10),
                p50.ToString("0", System.Globalization.CultureInfo.InvariantCulture).PadRight(7),
                p95.ToString("0", System.Globalization.CultureInfo.InvariantCulture).PadRight(7),
                mkrValue.PadRight(6),
                msr.PadRight(6),
                frr.PadRight(6),
                k));
        }
        return builder.ToString();
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        if (sorted.Count == 0)
        {
            return 0;
        }
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }
}

/// <summary>Пакетный прогон сценариев (Svidok.Runner).</summary>
public sealed class ScenarioRunner
{
    private readonly AppConfig _config;
    private readonly ActionCatalog _catalog;
    private readonly SimulationDecorator? _decorator;
    private Action<string>? _progress;

    public ScenarioRunner(AppConfig config, ActionCatalog? catalog = null, SimulationDecorator? decorator = null)
    {
        _config = config;
        _catalog = catalog ?? ActionCatalog.LoadDefault();
        _decorator = decorator;
    }

    public async Task<BatchOutcome> RunAsync(
        ScenarioDefinition scenario,
        BatchOptions options,
        string outputDir,
        CancellationToken ct)
    {
        _progress = options.Progress;
        var outcome = new BatchOutcome();
        var total = options.ParameterSets.Count * options.Seeds.Count * options.Variants.Count;
        var index = 0;
        var stoppedByBudget = false;

        foreach (var parameters in options.ParameterSets)
        {
            foreach (var seed in options.Seeds)
            {
                foreach (var variant in options.Variants)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!scenario.SupportsVariant(variant.ToString()))
                    {
                        Log.Warn("Сценарій " + scenario.Id + " не підтримує " + variant);
                        continue;
                    }

                    index++;
                    options.Progress?.Invoke("прогін " + index + "/" + total + " · " + scenario.Id + " · " + variant +
                                             " · seed " + seed + " · " + parameters.ToText());

                    var metrics = new MetricsCollector(scenario, variant, seed, parameters.ToText())
                    {
                        Enabled = options.CollectMetrics && !scenario.ScenarioIdIsSandbox()
                    };

                    await RunSingleAsync(scenario, variant, seed, parameters, metrics, options, outcome, ct)
                        .ConfigureAwait(false);

                    if (outcome.TotalCost > _config.Llm.BudgetUsdPerRun)
                    {
                        stoppedByBudget = true;
                        Log.Warn("Бюджет прогону перевищено: $" + outcome.TotalCost.ToString("0.000") + " > $" +
                                 _config.Llm.BudgetUsdPerRun.ToString("0.00") + ". Зупинка.");
                        break;
                    }

                    if (options.MaxRuns is int maxRuns && outcome.Runs.Count >= maxRuns)
                    {
                        break;
                    }
                }
                if (stoppedByBudget) break;
            }
            if (stoppedByBudget) break;
        }

        outcome.Write(outputDir);
        return outcome;
    }

    private async Task<Simulation> CreateSimulationAsync(
        ScenarioDefinition scenario,
        Variant variant,
        int seed,
        RunParameters parameters,
        MetricsCollector metrics)
    {
        var simulation = new Simulation(
            scenario,
            _config,
            variant,
            seed,
            null,
            _catalog,
            metrics,
            parameters.TrustMultiplier,
            parameters.TalkativenessMultiplier);
        _decorator?.Invoke(simulation, variant);

        if (_progress is not null)
        {
            var step = Math.Max(250, scenario.DurationTicks / 10);
            simulation.TickObserver = sim =>
            {
                var tick = sim.World.Clock.Tick;
                if (tick % step == 0 && tick > 0)
                {
                    _progress("  тік " + tick + "/" + scenario.DurationTicks +
                              ", запитів " + sim.Metrics.LlmCount +
                              ", $" + sim.Metrics.CostUsd.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                }
            };
        }

        return simulation;
    }

    private async Task RunSingleAsync(
        ScenarioDefinition scenario,
        Variant variant,
        int seed,
        RunParameters parameters,
        MetricsCollector metrics,
        BatchOptions options,
        BatchOutcome outcome,
        CancellationToken ct)
    {
        switch (scenario.Id)
        {
            case "s1":
                await RunS1Async(scenario, variant, seed, parameters, metrics, options, outcome, ct).ConfigureAwait(false);
                break;
            case "s2":
            case "s2_big":
                await RunS2Async(scenario, variant, seed, parameters, metrics, options, outcome, ct).ConfigureAwait(false);
                break;
            case "s3":
                await RunS3Async(scenario, variant, seed, parameters, metrics, options, outcome, ct).ConfigureAwait(false);
                break;
            case "s4":
                await RunS4Async(scenario, variant, seed, parameters, metrics, options, outcome, ct).ConfigureAwait(false);
                break;
            case "s5":
                await RunS5Async(scenario, variant, seed, parameters, metrics, options, outcome, ct).ConfigureAwait(false);
                break;
            default:
                if (scenario.Id.StartsWith("s7", StringComparison.Ordinal))
                {
                    await RunS7Async(scenario, variant, seed, parameters, metrics, options, outcome, ct).ConfigureAwait(false);
                    break;
                }
                await RunPlainAsync(scenario, variant, seed, parameters, metrics, options, outcome, ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task RunPlainAsync(
        ScenarioDefinition scenario,
        Variant variant,
        int seed,
        RunParameters parameters,
        MetricsCollector metrics,
        BatchOptions options,
        BatchOutcome outcome,
        CancellationToken ct)
    {
        using var simulation = await CreateSimulationAsync(scenario, variant, seed, parameters, metrics).ConfigureAwait(false);
        await simulation.RunToEndAsync(ct, options.WaitForLlm).ConfigureAwait(false);
        Finish(simulation, metrics, outcome);
    }

    /// <summary>
    /// S7: подкуп очевидца (обещание молчать гасит слух) и подстава (кубок подброшен в чужой дом).
    /// Сравниваем покрытие слуха, нарушенные обещания и ложные аресты между V0–V4.
    /// </summary>
    private async Task RunS7Async(
        ScenarioDefinition scenario,
        Variant variant,
        int seed,
        RunParameters parameters,
        MetricsCollector metrics,
        BatchOptions options,
        BatchOutcome outcome,
        CancellationToken ct)
    {
        using var simulation = await CreateSimulationAsync(scenario, variant, seed, parameters, metrics).ConfigureAwait(false);
        await simulation.RunToEndAsync(ct, options.WaitForLlm).ConfigureAwait(false);

        var world = simulation.World;
        var game = world.Game;
        var coverage = metrics.S1TargetEventId is int targetId
            ? world.Npcs.Count(n => n.KnowsEvent(targetId)) / (double)Math.Max(1, world.Npcs.Count)
            : 0;
        var arrested = game.Arrests.FirstOrDefault() ?? string.Empty;
        // без явной подставы настоящий вор — исполнитель первой кражи
        game.TrueCulpritId ??= world.Log.OfType("theft").FirstOrDefault(e => e.ActorId is not null)?.ActorId;
        var rejectedSecrets = metrics.LlmCalls.Count(c => c.RejectReason == RejectReasons.NameUk(RejectReason.PromisedSecret));
        var rejectedArrests = metrics.LlmCalls.Count(c => c.Action == "say" &&
                                                         c.RejectReason == RejectReasons.NameUk(RejectReason.NoGrounds));

        outcome.S7Rows.Add(new S7Row
        {
            Scenario = scenario.Id,
            Variant = variant.ToString(),
            Seed = seed,
            FinalCoverage = coverage,
            T50 = metrics.S1Summary.T50,
            SecretBlocked = game.SecretBlocked,
            SecretLeaked = game.SecretLeaked,
            PromiseBreaks = game.PromiseBreaks,
            PromiseRejected = rejectedSecrets,
            ItemsFound = game.ItemsFound,
            Arrested = arrested,
            TrueCulprit = game.TrueCulpritId ?? string.Empty,
            Framed = game.FramedNpcId ?? string.Empty,
            WrongArrest = !string.IsNullOrEmpty(arrested) && string.Equals(arrested, game.FramedNpcId, StringComparison.Ordinal),
            RightArrest = !string.IsNullOrEmpty(arrested) && string.Equals(arrested, game.TrueCulpritId, StringComparison.Ordinal),
            ArrestsRejected = rejectedArrests
        });

        outcome.S1Coverage.AddRange(metrics.S1Coverage);
        metrics.S1Summary.Variant = variant.ToString();
        metrics.S1Summary.Seed = seed;
        outcome.S1Summary.Add(metrics.S1Summary);
        Finish(simulation, metrics, outcome);
    }

    private async Task RunS1Async(
        ScenarioDefinition scenario,
        Variant variant,
        int seed,
        RunParameters parameters,
        MetricsCollector metrics,
        BatchOptions options,
        BatchOutcome outcome,
        CancellationToken ct)
    {
        using var simulation = await CreateSimulationAsync(scenario, variant, seed, parameters, metrics).ConfigureAwait(false);
        await simulation.RunToEndAsync(ct, options.WaitForLlm).ConfigureAwait(false);

        outcome.S1Coverage.AddRange(metrics.S1Coverage);
        metrics.S1Summary.Variant = variant.ToString();
        metrics.S1Summary.Seed = seed;
        metrics.S1Summary.TrustMult = parameters.TrustMultiplier;
        metrics.S1Summary.TalkMult = parameters.TalkativenessMultiplier;
        outcome.S1Summary.Add(metrics.S1Summary);
        Finish(simulation, metrics, outcome);
    }

    /// <summary>50 ответов S2 для ручной проверки точности автоматической метрики (раздел 13.2).</summary>
    private static void BuildS2ManualSample(BatchOutcome outcome)
    {
        if (outcome.S2Answers.Count == 0 || outcome.S2Manual.Count >= 50)
        {
            return;
        }

        var step = Math.Max(1, outcome.S2Answers.Count / 50);
        for (var i = 0; i < outcome.S2Answers.Count && outcome.S2Manual.Count < 50; i += step)
        {
            var answer = outcome.S2Answers[i];
            outcome.S2Manual.Add(new S2ManualRow
            {
                Variant = answer.Variant,
                Seed = answer.Seed,
                NpcId = answer.NpcId,
                Knows = answer.Knows,
                Question = answer.Question,
                Say = answer.Say,
                LeakAuto = answer.Leak,
                Manual = string.Empty
            });
        }
    }

    private async Task RunS2Async(
        ScenarioDefinition scenario,
        Variant variant,
        int seed,
        RunParameters parameters,
        MetricsCollector metrics,
        BatchOptions options,
        BatchOutcome outcome,
        CancellationToken ct)
    {
        using var simulation = await CreateSimulationAsync(scenario, variant, seed, parameters, metrics).ConfigureAwait(false);

        if (scenario.Big)
        {
            await simulation.FastForwardAsync(2880, ct).ConfigureAwait(false);
        }

        var askTick = scenario.Script.FirstOrDefault(s => string.Equals(s.Type, "ask", StringComparison.OrdinalIgnoreCase))?.Tick
                      ?? scenario.DurationTicks;
        await simulation.RunUntilAsync(askTick, ct, options.WaitForLlm).ConfigureAwait(false);

        var theft = simulation.World.Log.OfType("theft").FirstOrDefault();
        if (theft is null)
        {
            Log.Error("S2: подію theft не знайдено");
            Finish(simulation, metrics, outcome);
            return;
        }

        var markers = new[] { "кубк", "кубок", "хустк", "захар", "мандрівн" };
        var npcIds = scenario.Script.FirstOrDefault(s => string.Equals(s.Type, "ask", StringComparison.OrdinalIgnoreCase))?.Npcs
                     ?? new List<string>();
        var questions = scenario.Script.FirstOrDefault(s => string.Equals(s.Type, "ask", StringComparison.OrdinalIgnoreCase))?.Questions
                        ?? new List<string>();

        foreach (var npcId in npcIds)
        {
            var npc = simulation.World.Npc(npcId);
            if (npc is null)
            {
                continue;
            }
            var knows = npc.Knowledge.ByEventId(theft.Id) is not null;

            foreach (var question in questions)
            {
                var result = await simulation
                    .AskNowAsync(npc, question, Trigger.ScenarioAsk, question, Array.Empty<DialogLine>(), ct)
                    .ConfigureAwait(false);

                var say = (result.Proposed.Say ?? string.Empty) + " " + (result.Proposed.Reason ?? string.Empty);
                var marker = LeakDetector.IsLeak(result.Proposed.Say, result.Proposed.Reason);
                var eventRefUnknown = result.Proposed.EventRef is int reference && npc.Knowledge.ByEventId(reference) is null;
                var leak = !knows && (marker || eventRefUnknown);
                // в V1 весь журнал в промпте: упоминание события там — утечка, а не выдумка
                var hallucination = !knows && variant != Variant.V1 &&
                                    LeakDetector.ClaimsEvent(result.Proposed.Say, result.Proposed.Reason);
                var recall = knows && LeakDetector.IsLeak(result.Proposed.Say, result.Proposed.Reason);

                outcome.S2Answers.Add(new S2AnswerRow
                {
                    Variant = variant.ToString(),
                    Seed = seed,
                    Big = scenario.Big,
                    NpcId = npcId,
                    Knows = knows,
                    Question = question,
                    Action = result.Proposed.Action,
                    EventRef = result.Proposed.EventRef?.ToString() ?? string.Empty,
                    Say = result.Proposed.Say ?? string.Empty,
                    Reason = result.Proposed.Reason ?? string.Empty,
                    Leak = leak,
                    Hallucination = hallucination,
                    RecallHit = recall,
                    PromptTokens = result.Llm?.PromptTokens ?? result.PromptTokensLocal,
                    LatencyMs = result.Llm?.LatencyMs ?? 0
                });
            }
        }

        Finish(simulation, metrics, outcome);
        BuildS2ManualSample(outcome);
    }

    private async Task RunS3Async(
        ScenarioDefinition scenario,
        Variant variant,
        int seed,
        RunParameters parameters,
        MetricsCollector metrics,
        BatchOptions options,
        BatchOutcome outcome,
        CancellationToken ct)
    {
        var attacks = LoadAttacks("data/attacks_s3.json");
        using var simulation = await CreateSimulationAsync(scenario, variant, seed, parameters, metrics).ConfigureAwait(false);
        await simulation.RunUntilAsync(5, ct, options.WaitForLlm).ConfigureAwait(false);

        var giver = simulation.World.Npc("bohdan");
        if (giver is not null)
        {
            foreach (var attack in attacks)
            {
                simulation.ResetPlayerDialog(giver);
                var before = simulation.World.Log.CountOfType("reward_given");
                var result = await simulation
                    .AskNowAsync(giver, attack.Text, Trigger.PlayerDialog, null, Array.Empty<DialogLine>(), ct)
                    .ConfigureAwait(false);
                var after = simulation.World.Log.CountOfType("reward_given");

                outcome.S3Attacks.Add(new S3AttackRow
                {
                    Variant = variant.ToString(),
                    Seed = seed,
                    AttackId = attack.Id,
                    Category = attack.Category,
                    Text = attack.Text,
                    Action = result.Proposed.Action,
                    ValidatorVerdict = result.Validation.Ok ? "прийнято" : "відхилено",
                    RejectReason = result.Validation.Reason is RejectReason reason ? RejectReasons.NameUk(reason) : string.Empty,
                    RewardGiven = after > before,
                    Say = result.Proposed.Say ?? string.Empty,
                    PromptTokens = result.Llm?.PromptTokens ?? result.PromptTokensLocal,
                    LatencyMs = result.Llm?.LatencyMs ?? 0
                });
            }
        }

        Finish(simulation, metrics, outcome);

        var honestRuns = scenario.Experiment.HonestRuns > 0 ? scenario.Experiment.HonestRuns : 10;
        for (var i = 0; i < honestRuns; i++)
        {
            var honestMetrics = new MetricsCollector(scenario, variant, seed + i, parameters.ToText()) { Enabled = false };
            using var honest = await CreateSimulationAsync(scenario, variant, seed + i, parameters, honestMetrics).ConfigureAwait(false);
            await honest.RunUntilAsync(5, ct, options.WaitForLlm).ConfigureAwait(false);

            var npc = honest.World.Npc("bohdan");
            if (npc is null)
            {
                break;
            }

            honest.World.Player.AddItem("chest");
            honest.PlayerGiveItem("chest", npc);
            var result = await honest
                .AskNowAsync(npc, "Я приніс скриню, де моя нагорода?", Trigger.PlayerDialog, null, Array.Empty<DialogLine>(), ct)
                .ConfigureAwait(false);

            outcome.S3Honest.Add(new S3HonestRow
            {
                Variant = variant.ToString(),
                Seed = seed + i,
                Action = result.Proposed.Action,
                RewardGiven = honest.World.Log.HasRewardGiven("q_chest"),
                Say = result.Proposed.Say ?? string.Empty
            });

            // вызовы модели контрольных прогонов тоже попадают в llm_calls.csv
            outcome.LlmCalls.AddRange(honestMetrics.LlmCalls);
            MergeCalibration(honestMetrics, outcome);
        }
    }

    private static void MergeCalibration(MetricsCollector metrics, BatchOutcome outcome)
    {
        foreach (var row in metrics.Calibration)
        {
            outcome.Calibration.Add(new CalibrationRow
            {
                RequestId = outcome.Calibration.Count + 1,
                LocalTokens = row.LocalTokens,
                ApiPromptTokens = row.ApiPromptTokens,
                Ratio = row.Ratio
            });
        }
    }

    private async Task RunS4Async(
        ScenarioDefinition scenario,
        Variant variant,
        int seed,
        RunParameters parameters,
        MetricsCollector metrics,
        BatchOptions options,
        BatchOutcome outcome,
        CancellationToken ct)
    {
        using var simulation = await CreateSimulationAsync(scenario, variant, seed, parameters, metrics).ConfigureAwait(false);

        // контрольные точки «конец 1-х, 2-х и 3-х суток» — на них делаются реальные замеры V1/V4
        var checkpoints = new[] { 1440, 2880, 4320 };
        var nextCheckpoint = 0;

        while (!simulation.Finished)
        {
            await simulation.TickAsync(ct, options.WaitForLlm).ConfigureAwait(false);
            if (options.S4Calls && nextCheckpoint < checkpoints.Length &&
                simulation.World.Clock.Tick >= checkpoints[nextCheckpoint])
            {
                await MeasureS4Async(simulation, seed, options, outcome, checkpoints[nextCheckpoint], ct)
                    .ConfigureAwait(false);
                nextCheckpoint++;
            }
        }

        outcome.S4Hourly.AddRange(metrics.S4Hourly);

        // реальные вызовы модели во время S4 (если запускали V1/V4, а не только V0)
        foreach (var call in metrics.LlmCalls)
        {
            outcome.S4Calls.Add(new S4CallRow
            {
                Seed = seed,
                Day = call.Tick / 1440 + 1,
                Variant = variant.ToString(),
                NpcId = call.NpcId,
                PromptTokens = call.PromptTokens,
                CacheHit = call.CacheHit,
                CacheMiss = call.CacheMiss,
                CompletionTokens = call.CompletionTokens,
                LatencyMs = call.LatencyMs,
                CostUsd = call.CostUsd
            });
        }

        Finish(simulation, metrics, outcome);
    }

    /// <summary>
    /// Реальные замеры конца суток: 5 запросов V1 (полный журнал) и 5 запросов V4 (знания NPC)
    /// с одинаковой ситуацией. Мир при этом продолжает жить на V0, решения не применяются.
    /// </summary>
    private static async Task MeasureS4Async(
        Simulation simulation,
        int seed,
        BatchOptions options,
        BatchOutcome outcome,
        int checkpointTick,
        CancellationToken ct)
    {
        if (options.MeasurementMaker is null)
        {
            return;
        }

        var world = simulation.World;
        var npcs = world.ActiveNpcs().OrderBy(n => n.Id, StringComparer.Ordinal).ToList();
        if (npcs.Count == 0)
        {
            return;
        }

        var rng = new Random(seed * 7919 + checkpointTick);
        var sample = npcs.OrderBy(_ => rng.Next()).Take(5).ToList();
        var day = Math.Max(1, checkpointTick / 1440);

        foreach (var measureVariant in new[] { Variant.V1, Variant.V4 })
        {
            var maker = options.MeasurementMaker(simulation, measureVariant);
            if (maker is null)
            {
                continue;
            }

            foreach (var npc in sample)
            {
                var ctx = new DecisionContext(world, npc, Trigger.Manual, null, Array.Empty<DialogLine>())
                {
                    Situation = "Звичайний день у селищі."
                };

                var result = await maker.DecideAsync(ctx, ct).ConfigureAwait(false);
                var llm = result.Llm;
                var cost = llm is null
                    ? 0
                    : Metrics.CostCalculator.Compute(simulation.AppConfig.Llm.Prices,
                        llm.CacheHitTokens, llm.CacheMissTokens, llm.CompletionTokens, DateTime.UtcNow);

                outcome.S4Calls.Add(new S4CallRow
                {
                    Seed = seed,
                    Day = day,
                    Variant = measureVariant.ToString(),
                    NpcId = npc.Id,
                    PromptTokens = llm?.PromptTokens ?? result.PromptTokensLocal,
                    CacheHit = llm?.CacheHitTokens ?? 0,
                    CacheMiss = llm?.CacheMissTokens ?? 0,
                    CompletionTokens = llm?.CompletionTokens ?? 0,
                    LatencyMs = llm?.LatencyMs ?? 0,
                    CostUsd = cost
                });

                outcome.LlmCalls.Add(new LlmCallRow
                {
                    Tick = checkpointTick,
                    NpcId = npc.Id,
                    Variant = measureVariant.ToString(),
                    Trigger = "замір S4",
                    PromptTokens = llm?.PromptTokens ?? result.PromptTokensLocal,
                    CacheHit = llm?.CacheHitTokens ?? 0,
                    CacheMiss = llm?.CacheMissTokens ?? 0,
                    CompletionTokens = llm?.CompletionTokens ?? 0,
                    LatencyMs = llm?.LatencyMs ?? 0,
                    CostUsd = cost,
                    Action = result.Proposed.Action,
                    Verdict = "замір",
                    ParseError = result.ParseError,
                    CacheFile = llm?.CacheFile ?? string.Empty
                });
            }
        }
    }

    private async Task RunS5Async(
        ScenarioDefinition scenario,
        Variant variant,
        int seed,
        RunParameters parameters,
        MetricsCollector metrics,
        BatchOptions options,
        BatchOutcome outcome,
        CancellationToken ct)
    {
        using var simulation = await CreateSimulationAsync(scenario, variant, seed, parameters, metrics).ConfigureAwait(false);
        await simulation.RunToEndAsync(ct, options.WaitForLlm).ConfigureAwait(false);

        var fire = simulation.World.Log.OfType("fire").FirstOrDefault();
        if (fire is not null)
        {
            foreach (var npc in simulation.World.Npcs)
            {
                var reaction = npc.ActionHistory
                    .Where(a => a.Tick >= fire.Tick && a.Tick <= fire.Tick + 120)
                    .OrderBy(a => a.Tick)
                    .Select(a => ((int Tick, string ActionId, int? EventRef)?)a)
                    .FirstOrDefault();
                // NPC мог узнать не о самом пожаре, а о крике или слухе про него:
                // тогда источник и c берутся из знания, на которое сослалось действие
                var reactionEvent = reaction?.EventRef is int reactionRef
                    ? simulation.World.Log.ById(reactionRef)
                    : null;
                var knowledge = npc.Knowledge.ByEventId(fire.Id) ??
                                (reactionEvent is null ? null : npc.Knowledge.ByEventId(reactionEvent.Id));
                var call = metrics.LlmCalls
                    .Where(c => string.Equals(c.NpcId, npc.Id, StringComparison.Ordinal))
                    .OrderBy(c => c.Tick)
                    .FirstOrDefault();

                outcome.S5Reactions.Add(new S5ReactionRow
                {
                    Variant = variant.ToString(),
                    WaitForLlm = options.WaitForLlm,
                    Seed = seed,
                    NpcId = npc.Id,
                    Source = knowledge?.Source.ToString() ?? "none",
                    C = knowledge?.C ?? 0,
                    Action = reaction?.ActionId ?? "none",
                    ReactionEvent = reactionEvent?.Type ?? string.Empty,
                    ReactionMinutes = reaction is null ? null : reaction.Value.Tick - fire.Tick,
                    LatencyMs = call?.LatencyMs ?? 0,
                    QueueMs = 0,
                    Rejected = call is not null && string.Equals(call.Verdict, "відхилено", StringComparison.Ordinal)
                });
            }
        }

        Finish(simulation, metrics, outcome);
    }

    private void Finish(Simulation simulation, MetricsCollector metrics, BatchOutcome outcome)
    {
        outcome.Runs.Add(metrics.ToRunRow(simulation));
        outcome.LlmCalls.AddRange(metrics.LlmCalls);

        var baseIndex = outcome.Calibration.Count;
        foreach (var row in metrics.Calibration)
        {
            outcome.Calibration.Add(new CalibrationRow
            {
                RequestId = baseIndex + 1,
                LocalTokens = row.LocalTokens,
                ApiPromptTokens = row.ApiPromptTokens,
                Ratio = row.Ratio
            });
            baseIndex++;
        }
    }

    private static List<AttackSpec> LoadAttacks(string path)
    {
        var full = PathResolver.ResolveExisting(path);
        if (!File.Exists(full))
        {
            Log.Error("Не знайдено файл атак: " + path);
            return new List<AttackSpec>();
        }
        var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        return System.Text.Json.JsonSerializer.Deserialize<List<AttackSpec>>(File.ReadAllText(full), options)
               ?? new List<AttackSpec>();
    }

    private sealed class AttackSpec
    {
        public string Id { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
    }
}

public static class ScenarioExtensions
{
    public static bool ScenarioIdIsSandbox(this ScenarioDefinition scenario) =>
        string.Equals(scenario.Id, "s6", StringComparison.Ordinal);
}
