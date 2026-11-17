using System.Globalization;
using System.Text;
using Svidok.Core.Config;
using Svidok.Core.Decisions;
using Svidok.Core.Scenarios;
using Svidok.Core.Util;
using Svidok.Llm;

namespace Svidok.Runner;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        try
        {
            var options = CliOptions.Parse(args);
            if (options.ShowHelp)
            {
                CliOptions.PrintHelp();
                return 0;
            }

            var config = AppConfig.LoadDefault();

            if (!string.IsNullOrWhiteSpace(options.AnalyzeS2))
            {
                return S2Analyzer.Run(options.AnalyzeS2!);
            }

            if (!string.IsNullOrWhiteSpace(options.AnalyzeS3))
            {
                return S3Analyzer.Run(options.AnalyzeS3!, config);
            }
            if (options.Budget is double budgetLimit)
            {
                config.Llm.BudgetUsdPerRun = budgetLimit;
            }

            if (!string.IsNullOrWhiteSpace(options.LlmMode))
            {
                config.Llm.Mode = options.LlmMode!;
            }

            if (options.ScenarioId is null)
            {
                CliOptions.PrintScenarios();
                return 0;
            }

            var scenario = ScenarioLoader.FindById(options.ScenarioId);
            if (scenario is null)
            {
                Console.Error.WriteLine("Невідомий сценарій: " + options.ScenarioId);
                CliOptions.PrintScenarios();
                return 1;
            }

            var variants = options.SelectedVariants.Count > 0
                ? options.SelectedVariants
                : scenario.RecommendedVariants.Count > 0
                    ? scenario.RecommendedVariants.Select(Variants.Parse).ToList()
                    : new List<Variant> { Variant.V0 };

            var seeds = options.Seeds.Count > 0 ? options.Seeds : scenario.Experiment.Seeds.Count > 0
                ? scenario.Experiment.Seeds
                : new List<int> { 1 };

            var parameterSets = BuildParameterSets(options);
            var outDir = options.OutputDir ?? Path.Combine("results",
                scenario.Id + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm", CultureInfo.InvariantCulture));

            var mode = config.Llm.ResolveMode();
            Console.WriteLine("Сценарій: " + scenario.Id + " — " + scenario.NameUk);
            Console.WriteLine("Варіанти: " + string.Join(", ", variants));
            Console.WriteLine("Seed: " + string.Join(", ", seeds));
            Console.WriteLine("Режим моделі: " + mode);
            if (mode is LlmMode.Live or LlmMode.LiveWithCache && !LlmClientFactory.HasApiKey(config.Llm))
            {
                Console.WriteLine("Помилка: змінна " + config.Llm.ApiKeyEnv + " не задана, живі запити неможливі.");
                Console.WriteLine("Для відтворення лише з кешу використовуй --llm ReplayOnly.");
                return 1;
            }
            Console.WriteLine("Результати: " + outDir);
            Console.WriteLine();

            var counter = new TokenCounter();
            var runner = new ScenarioRunner(config, null,
                (simulation, variant) => LlmBootstrap.Create(simulation, variant, config.Llm, counter, options.DryRun));

            var batch = new BatchOptions
            {
                Variants = variants,
                Seeds = seeds,
                ParameterSets = parameterSets,
                DryRun = options.DryRun,
                WaitForLlm = options.WaitForLlm,
                CollectMetrics = true,
                S4Calls = options.S4Calls,
                MeasurementMaker = options.S4Calls
                    ? (simulation, variant) => LlmBootstrap.CreateStandalone(simulation, variant, config.Llm, counter, options.DryRun)
                    : null,
                Progress = line => Console.WriteLine(line)
            };

            var outcome = await runner.RunAsync(scenario, batch, outDir, CancellationToken.None);

            Console.WriteLine();
            Console.WriteLine("Готово. Прогонів: " + outcome.Runs.Count +
                              ", запитів: " + outcome.TotalLlmCalls +
                              ", вартість: $" + outcome.TotalCost.ToString("0.0000", CultureInfo.InvariantCulture));
            Console.WriteLine(outcome.BuildSummaryTable());
            Console.WriteLine("CSV: " + Path.GetFullPath(outDir));
            return 0;
        }
        catch (LlmFatalException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Log.Error(ex.Message);
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Прогін скасовано.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Помилка: " + ex);
            Log.Error("Помилка прогону", ex);
            return 1;
        }
    }

    private static List<RunParameters> BuildParameterSets(CliOptions options)
    {
        var trust = options.Params.TryGetValue("trustMultiplier", out var t) && t.Count > 0 ? t : new List<double> { 1.0 };
        var talk = options.Params.TryGetValue("talkativenessMultiplier", out var k) && k.Count > 0 ? k : new List<double> { 1.0 };

        var result = new List<RunParameters>();
        foreach (var trustValue in trust)
        {
            foreach (var talkValue in talk)
            {
                result.Add(new RunParameters
                {
                    TrustMultiplier = trustValue,
                    TalkativenessMultiplier = talkValue
                });
            }
        }
        return result;
    }
}

public sealed class CliOptions
{
    public string? ScenarioId { get; private set; }

    public List<Variant> SelectedVariants { get; } = new();

    public List<int> Seeds { get; } = new();

    public string? LlmMode { get; private set; }

    public string? OutputDir { get; private set; }

    public Dictionary<string, List<double>> Params { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool DryRun { get; private set; }

    public bool WaitForLlm { get; private set; } = true;

    public bool ShowHelp { get; private set; }

    public bool S4Calls { get; private set; }

    public double? Budget { get; private set; }

    public string? AnalyzeS3 { get; private set; }

    public string? AnalyzeS2 { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var options = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--help":
                case "-h":
                    options.ShowHelp = true;
                    break;
                case "--scenario":
                    options.ScenarioId = Next(args, ref i);
                    break;
                case "--variants":
                    foreach (var part in Split(Next(args, ref i)))
                    {
                        options.SelectedVariants.Add(Variants.Parse(part));
                    }
                    break;
                case "--seeds":
                    options.Seeds.AddRange(ParseSeeds(Next(args, ref i)));
                    break;
                case "--llm":
                    options.LlmMode = Next(args, ref i);
                    break;
                case "--out":
                    options.OutputDir = Next(args, ref i);
                    break;
                case "--param":
                    ParseParam(Next(args, ref i), options.Params);
                    break;
                case "--analyze-s2":
                    options.AnalyzeS2 = Next(args, ref i);
                    break;
                case "--analyze-s3":
                    options.AnalyzeS3 = Next(args, ref i);
                    break;
                case "--budget":
                    options.Budget = double.TryParse(Next(args, ref i), NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var budget)
                        ? budget
                        : null;
                    break;
                case "--s4-calls":
                    options.S4Calls = true;
                    break;
                case "--dry-run":
                    options.DryRun = true;
                    break;
                case "--no-wait":
                    options.WaitForLlm = false;
                    break;
                case "--wait":
                    options.WaitForLlm = true;
                    break;
                default:
                    if (arg.StartsWith("--scenario=", StringComparison.Ordinal))
                    {
                        options.ScenarioId = arg["--scenario=".Length..];
                    }
                    else if (arg.StartsWith("--variants=", StringComparison.Ordinal))
                    {
                        foreach (var part in Split(arg["--variants=".Length..]))
                        {
                            options.SelectedVariants.Add(Variants.Parse(part));
                        }
                    }
                    else if (arg.StartsWith("--seeds=", StringComparison.Ordinal))
                    {
                        options.Seeds.AddRange(ParseSeeds(arg["--seeds=".Length..]));
                    }
                    else if (arg.StartsWith("--llm=", StringComparison.Ordinal))
                    {
                        options.LlmMode = arg["--llm=".Length..];
                    }
                    else if (arg.StartsWith("--out=", StringComparison.Ordinal))
                    {
                        options.OutputDir = arg["--out=".Length..];
                    }
                    else if (arg.StartsWith("--param=", StringComparison.Ordinal))
                    {
                        ParseParam(arg["--param=".Length..], options.Params);
                    }
                    else if (!string.IsNullOrWhiteSpace(arg))
                    {
                        options.ScenarioId ??= arg;
                    }
                    break;
            }
        }
        return options;
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Svidok.Runner — пакетний прогін сценаріїв без графіки");
        Console.WriteLine();
        Console.WriteLine("  --scenario <id>            s1 | s2 | s2_big | s3 | s4 | s5 | s6");
        Console.WriteLine("  --variants V0,V1,V2,V3,V4  список варіантів (за замовчуванням — рекомендовані)");
        Console.WriteLine("  --seeds 1-3 | 1,2,3        seed прогонів");
        Console.WriteLine("  --llm Live|LiveWithCache|ReplayOnly|Mock");
        Console.WriteLine("  --out results/s3_2026-11-20");
        Console.WriteLine("  --param trustMultiplier=0.5,0.7,0.9");
        Console.WriteLine("  --param talkativenessMultiplier=0.5,1.0");
        Console.WriteLine("  --budget 5                 ліміт витрат на прогін, $" );
        Console.WriteLine("  --analyze-s2 results/s2_x  офлайн: перерахувати утечки та галюцинації S2");
        Console.WriteLine("  --analyze-s3 results/s3_x  офлайн: прогнати збережені give_reward через валідатор");
        Console.WriteLine("  --s4-calls                 S4: реальні заміри V1/V4 наприкінці 1-3 діб");
        Console.WriteLine("  --dry-run                  порахувати запити й вартість без мережі");
        Console.WriteLine("  --no-wait                  не чекати відповідей моделі на кожному тику");
        Console.WriteLine("  --help                     ця довідка");
        Console.WriteLine();
        PrintScenarios();
    }

    public static void PrintScenarios()
    {
        Console.WriteLine("Сценарії:");
        foreach (var scenario in ScenarioLoader.LoadAll())
        {
            Console.WriteLine("  " + scenario.Id.PadRight(8) + scenario.NameUk + " — " + scenario.WhatChecksUk);
        }
    }

    private static string? Next(string[] args, ref int index)
    {
        if (index + 1 >= args.Length)
        {
            return null;
        }
        index++;
        return args[index];
    }

    private static IEnumerable<string> Split(string? value) =>
        (value ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static List<int> ParseSeeds(string? value)
    {
        var seeds = new List<int>();
        foreach (var part in Split(value))
        {
            var dash = part.IndexOf('-');
            if (dash > 0 &&
                int.TryParse(part[..dash], out var from) &&
                int.TryParse(part[(dash + 1)..], out var to) &&
                to >= from && to - from < 1000)
            {
                for (var seed = from; seed <= to; seed++)
                {
                    seeds.Add(seed);
                }
                continue;
            }
            if (int.TryParse(part, out var single))
            {
                seeds.Add(single);
            }
        }
        return seeds;
    }

    private static void ParseParam(string? value, Dictionary<string, List<double>> target)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }
        var pair = value.Split('=', 2);
        if (pair.Length != 2)
        {
            return;
        }
        var values = new List<double>();
        foreach (var part in Split(pair[1]))
        {
            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                values.Add(number);
            }
        }
        if (values.Count > 0)
        {
            target[pair[0].Trim()] = values;
        }
    }
}
