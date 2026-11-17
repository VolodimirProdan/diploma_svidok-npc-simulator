using System.Diagnostics;
using Svidok.Core.Decisions;
using Svidok.Core.Entities;
using Svidok.Core.Events;
using Svidok.Core.Knowledge;
using Svidok.Core.Scenarios;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Metrics;

/// <summary>Сбор метрик раздела 14 для одного прогона (мире/сценарии).</summary>
public sealed class MetricsCollector
{
    private readonly Stopwatch _clock = new();
    private readonly List<long> _latencies = new();
    private readonly Dictionary<int, double> _coverageByTick = new();

    public MetricsCollector(ScenarioDefinition scenario, Variant variant, int seed, string paramsText)
    {
        ScenarioId = scenario.Id;
        VariantName = variant.ToString();
        Seed = seed;
        ParamsText = paramsText;
        Big = scenario.Big;
    }

    public string ScenarioId { get; }

    public string VariantName { get; }

    public int Seed { get; }

    public string ParamsText { get; }

    public bool Big { get; }

    /// <summary>S6 — демонстрация, метрик нет.</summary>
    public bool Enabled { get; set; } = true;

    public int LlmCount { get; private set; }

    public int PromptTokens { get; private set; }

    public int CacheHitTokens { get; private set; }

    public int CacheMissTokens { get; private set; }

    public int CompletionTokens { get; private set; }

    public double CostUsd { get; private set; }

    public int ParseErrors { get; private set; }

    public int Rejections { get; private set; }

    public IReadOnlyList<long> Latencies => _latencies;

    public List<LlmCallRow> LlmCalls { get; } = new();

    public List<CalibrationRow> Calibration { get; } = new();

    public List<S1CoverageRow> S1Coverage { get; } = new();

    public S1SummaryRow S1Summary { get; } = new();

    public List<S4HourRow> S4Hourly { get; } = new();

    public List<S4CallRow> S4Calls { get; } = new();

    public List<S5ReactionRow> S5Reactions { get; } = new();

    public List<S2AnswerRow> S2Answers { get; } = new();

    public List<S3AttackRow> S3Attacks { get; } = new();

    public List<S3HonestRow> S3Honest { get; } = new();

    public double TrustMult { get; set; } = 1.0;

    public double TalkMult { get; set; } = 1.0;

    public int? S1TargetEventId { get; private set; }

    /// <summary>Покрытие слуха о первой краже считается в S1 и в S7 (подкуп и подстава).</summary>
    private bool IsCoverageScenario =>
        string.Equals(ScenarioId, "s1", StringComparison.Ordinal) ||
        ScenarioId.StartsWith("s7", StringComparison.Ordinal);

    public int S1TargetEventTick { get; private set; }

    public string S1TrueActor { get; private set; } = string.Empty;

    public List<string> S1Participants { get; } = new();

    public double S1MeanC14 { get; private set; }

    public double S1MeanC22 { get; private set; }

    public Dictionary<int, double> CoverageHistory => _coverageByTick;

    public void Start() => _clock.Start();

    public double ElapsedSeconds => _clock.Elapsed.TotalSeconds;

    public void SetS1Target(WorldEvent target, string trueActor, IEnumerable<string> participants)
    {
        S1TargetEventId = target.Id;
        S1TargetEventTick = target.Tick;
        S1TrueActor = trueActor;
        S1Participants.Clear();
        S1Participants.AddRange(participants);
        S1Summary.TrustMult = TrustMult;
        S1Summary.TalkMult = TalkMult;
    }

    public void OnEvent(WorldEvent worldEvent)
    {
        if (S1TargetEventId is null &&
            IsCoverageScenario &&
            string.Equals(worldEvent.Type, "theft", StringComparison.Ordinal))
        {
            var participants = new List<string>();
            if (!string.IsNullOrWhiteSpace(worldEvent.ActorId))
            {
                participants.Add(worldEvent.ActorId);
            }
            if (!string.IsNullOrWhiteSpace(worldEvent.TargetId))
            {
                participants.Add(worldEvent.TargetId);
            }
            SetS1Target(worldEvent, worldEvent.ActorId ?? string.Empty, participants);
        }

        if (string.Equals(worldEvent.Type, "arrest", StringComparison.Ordinal) && S1Summary.ArrestTick is null)
        {
            S1Summary.ArrestTick = worldEvent.Tick;
            S1Summary.ArrestedId = worldEvent.TargetId ?? string.Empty;
        }
    }

    public void OnTick(Simulation simulation)
    {
        if (!Enabled)
        {
            return;
        }

        var world = simulation.World;
        if (IsCoverageScenario && S1TargetEventId is int targetId)
        {
            if (world.Tick % 10 == 0)
            {
                var sample = SampleCoverage(world, targetId);
                if (sample is not null)
                {
                    S1Coverage.Add(new S1CoverageRow
                    {
                        Variant = VariantName,
                        Seed = Seed,
                        TrustMult = TrustMult,
                        TalkMult = TalkMult,
                        Tick = world.Tick,
                        Coverage = sample.Value.Coverage,
                        MeanC = sample.Value.MeanC,
                        Distorted = sample.Value.Distorted,
                        WrongSuspect = sample.Value.WrongSuspect
                    });
                    _coverageByTick[world.Tick] = sample.Value.Coverage;

                    if (S1Summary.T50 is null && sample.Value.Coverage >= 0.5)
                    {
                        S1Summary.T50 = world.Tick - S1TargetEventTick;
                    }
                    if (S1Summary.T90 is null && sample.Value.Coverage >= 0.9)
                    {
                        S1Summary.T90 = world.Tick - S1TargetEventTick;
                    }
                }
            }

            if (world.Clock.MinuteOfDay == 14 * 60)
            {
                S1MeanC14 = SampleCoverage(world, targetId)?.MeanC ?? 0;
                S1Summary.MeanC14 = S1MeanC14;
            }
            if (world.Clock.MinuteOfDay >= 22 * 60 - 1)
            {
                S1MeanC22 = SampleCoverage(world, targetId)?.MeanC ?? 0;
                S1Summary.MeanC22 = S1MeanC22;
            }
        }

        if (string.Equals(ScenarioId, "s4", StringComparison.Ordinal) && world.Clock.MinuteOfDay % 60 == 0)
        {
            CollectS4Hour(simulation);
        }
    }

    private void CollectS4Hour(Simulation simulation)
    {
        var world = simulation.World;
        var json = world.Log.ToJsonBytes();
        var text = System.Text.Encoding.UTF8.GetString(json);
        var tokensFull = simulation.EstimateTokens(text);

        var totals = new List<int>();
        if (simulation.NpcPromptBuilder is not null)
        {
            foreach (var npc in world.ActiveNpcs())
            {
                var prompt = simulation.NpcPromptBuilder(npc);
                totals.Add(simulation.EstimateTokens(prompt));
            }
        }

        var mean = totals.Count > 0 ? (int)Math.Round(totals.Average()) : 0;
        var min = totals.Count > 0 ? totals.Min() : 0;
        var max = totals.Count > 0 ? totals.Max() : 0;
        var k = mean > 0 ? (double)tokensFull / mean : 0;

        S4Hourly.Add(new S4HourRow
        {
            Seed = Seed,
            Hour = world.Clock.HourIndex,
            Events = world.Log.Count,
            LogBytes = json.LongLength,
            TokensFull = tokensFull,
            TokensNpcMean = mean,
            TokensNpcMin = min,
            TokensNpcMax = max,
            K = k
        });
    }

    private (double Coverage, double MeanC, double Distorted, double WrongSuspect)? SampleCoverage(World world, int eventId)
    {
        var participants = new HashSet<string>(S1Participants, StringComparer.Ordinal);
        var observers = world.ActiveNpcs().Where(n => !participants.Contains(n.Id)).ToList();
        if (observers.Count == 0)
        {
            return null;
        }

        var known = new List<KnowledgeEntry>();
        foreach (var npc in observers)
        {
            var entry = npc.Knowledge.ByEventId(eventId);
            if (entry is not null)
            {
                known.Add(entry);
            }
        }

        var coverage = (double)known.Count / observers.Count;
        var meanC = known.Count > 0 ? known.Average(e => e.C) : 0;
        var distorted = known.Count > 0 ? (double)known.Count(e => e.IsDistorted) / known.Count : 0;
        var wrong = known.Count > 0
            ? (double)known.Count(e =>
            {
                var actor = e.Field("actor");
                return !string.IsNullOrEmpty(actor) &&
                       !string.Equals(actor, S1TrueActor, StringComparison.Ordinal) &&
                       actor is not ("невідомий" or "хтось");
            }) / known.Count
            : 0;

        S1Summary.DistortedShare = distorted;
        S1Summary.WrongSuspectShare = wrong;
        return (coverage, meanC, distorted, wrong);
    }

    public void RecordLlmCall(int tick, string npcId, string trigger, LlmDecision executed, ValidationResult validation,
        bool parseError, int promptTokensLocal, Decisions.DecisionResult result, string cacheFile, double costUsd)
    {
        var llm = result.Llm;
        if (llm is not null)
        {
            LlmCount++;
            PromptTokens += llm.PromptTokens;
            CacheHitTokens += llm.CacheHitTokens;
            CacheMissTokens += llm.CacheMissTokens;
            CompletionTokens += llm.CompletionTokens;
            CostUsd += costUsd;
            if (llm.LatencyMs > 0)
            {
                _latencies.Add(llm.LatencyMs);
            }
        }

        if (parseError)
        {
            ParseErrors++;
        }
        if (!validation.Ok)
        {
            Rejections++;
        }

        LlmCalls.Add(new LlmCallRow
        {
            Tick = tick,
            NpcId = npcId,
            Variant = VariantName,
            Trigger = trigger,
            PromptTokens = llm?.PromptTokens ?? 0,
            CacheHit = llm?.CacheHitTokens ?? 0,
            CacheMiss = llm?.CacheMissTokens ?? 0,
            CompletionTokens = llm?.CompletionTokens ?? 0,
            LatencyMs = llm?.LatencyMs ?? 0,
            CostUsd = costUsd,
            Action = executed.Action,
            Verdict = validation.Ok ? "виконано" : "відхилено",
            RejectReason = validation.Reason is RejectReason reason ? RejectReasons.NameUk(reason) : string.Empty,
            ParseError = parseError,
            CacheFile = cacheFile
        });

        if (Calibration.Count < 20 && llm is not null && llm.PromptTokens > 0 && promptTokensLocal > 0)
        {
            Calibration.Add(new CalibrationRow
            {
                RequestId = Calibration.Count + 1,
                LocalTokens = promptTokensLocal,
                ApiPromptTokens = llm.PromptTokens,
                Ratio = (double)llm.PromptTokens / promptTokensLocal
            });
        }
    }

    public double LatencyPercentile(double percentile)
    {
        if (_latencies.Count == 0)
        {
            return 0;
        }
        var sorted = _latencies.OrderBy(v => v).ToList();
        var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
    }

    public RunRow ToRunRow(Simulation simulation)
    {
        return new RunRow
        {
            Scenario = ScenarioId,
            Variant = VariantName,
            Seed = Seed,
            Params = ParamsText,
            Ticks = simulation.World.Clock.Tick,
            Events = simulation.World.Log.Count,
            LogBytes = simulation.World.Log.ByteSize,
            LlmCalls = LlmCount,
            PromptTokens = PromptTokens,
            CacheHitTokens = CacheHitTokens,
            CompletionTokens = CompletionTokens,
            CostUsd = CostUsd,
            ParseErrors = ParseErrors,
            Rejections = Rejections,
            DurationSec = ElapsedSeconds
        };
    }
}
