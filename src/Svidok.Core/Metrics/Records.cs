namespace Svidok.Core.Metrics;

public sealed class RunRow
{
    public string Scenario { get; set; } = string.Empty;

    public string Variant { get; set; } = string.Empty;

    public int Seed { get; set; }

    public string Params { get; set; } = string.Empty;

    public int Ticks { get; set; }

    public int Events { get; set; }

    public long LogBytes { get; set; }

    public int LlmCalls { get; set; }

    public int PromptTokens { get; set; }

    public int CacheHitTokens { get; set; }

    public int CompletionTokens { get; set; }

    public double CostUsd { get; set; }

    public int ParseErrors { get; set; }

    public int Rejections { get; set; }

    public double DurationSec { get; set; }
}

public sealed class S1CoverageRow
{
    public string Scenario { get; set; } = "s1";

    public string Variant { get; set; } = string.Empty;

    public int Seed { get; set; }

    public double TrustMult { get; set; }

    public double TalkMult { get; set; }

    public int Tick { get; set; }

    public double Coverage { get; set; }

    public double MeanC { get; set; }

    public double Distorted { get; set; }

    public double WrongSuspect { get; set; }
}

public sealed class S1SummaryRow
{
    public string Variant { get; set; } = string.Empty;

    public int Seed { get; set; }

    public double TrustMult { get; set; }

    public double TalkMult { get; set; }

    public int? T50 { get; set; }

    public int? T90 { get; set; }

    public double MeanC14 { get; set; }

    public double MeanC22 { get; set; }

    public double DistortedShare { get; set; }

    public double WrongSuspectShare { get; set; }

    public int? ArrestTick { get; set; }

    public string ArrestedId { get; set; } = string.Empty;
}

public sealed class S2AnswerRow
{
    public string Variant { get; set; } = string.Empty;

    public int Seed { get; set; }

    public bool Big { get; set; }

    public string NpcId { get; set; } = string.Empty;

    public bool Knows { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string EventRef { get; set; } = string.Empty;

    public string Say { get; set; } = string.Empty;

    public string Reason { get; set; } = string.Empty;

    public bool Leak { get; set; }

    public bool RecallHit { get; set; }

    /// <summary>Положительное упоминание маркера без знания: выдумка модели, а не утечка.</summary>
    public bool Hallucination { get; set; }

    public int PromptTokens { get; set; }

    public long LatencyMs { get; set; }
}

/// <summary>Строка ручной проверки S2: человек ставит 1 (утечка) или 0 в колонку manual.</summary>
public sealed class S2ManualRow
{
    public string Variant { get; set; } = string.Empty;

    public int Seed { get; set; }

    public string NpcId { get; set; } = string.Empty;

    public bool Knows { get; set; }

    public string Question { get; set; } = string.Empty;

    public string Say { get; set; } = string.Empty;

    public bool LeakAuto { get; set; }

    public string Manual { get; set; } = string.Empty;
}

public sealed class S3AttackRow
{    public string Variant { get; set; } = string.Empty;

    public int Seed { get; set; }

    public string AttackId { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string Action { get; set; } = string.Empty;

    public string ValidatorVerdict { get; set; } = string.Empty;

    public string RejectReason { get; set; } = string.Empty;

    public bool RewardGiven { get; set; }

    public string Say { get; set; } = string.Empty;

    public int PromptTokens { get; set; }

    public long LatencyMs { get; set; }
}

public sealed class S3HonestRow
{
    public string Variant { get; set; } = string.Empty;

    public int Seed { get; set; }

    public string Action { get; set; } = string.Empty;

    public bool RewardGiven { get; set; }

    public string Say { get; set; } = string.Empty;
}

public sealed class S4HourRow
{
    public int Seed { get; set; }

    public int Hour { get; set; }

    public int Events { get; set; }

    public long LogBytes { get; set; }

    public int TokensFull { get; set; }

    public int TokensNpcMean { get; set; }

    public int TokensNpcMin { get; set; }

    public int TokensNpcMax { get; set; }

    public double K { get; set; }
}

public sealed class S4CallRow
{
    public int Seed { get; set; }

    public int Day { get; set; }

    public string Variant { get; set; } = string.Empty;

    public string NpcId { get; set; } = string.Empty;

    public int PromptTokens { get; set; }

    public int CacheHit { get; set; }

    public int CacheMiss { get; set; }

    public int CompletionTokens { get; set; }

    public long LatencyMs { get; set; }

    public double CostUsd { get; set; }
}

public sealed class S5ReactionRow
{
    public string Variant { get; set; } = string.Empty;

    public bool WaitForLlm { get; set; }

    public int Seed { get; set; }

    public string NpcId { get; set; } = string.Empty;

    public string Source { get; set; } = string.Empty;

    public double C { get; set; }

    public string Action { get; set; } = string.Empty;

    /// <summary>Тип события, на которое сослалось действие (fire, shout...).</summary>
    public string ReactionEvent { get; set; } = string.Empty;

    public int? ReactionMinutes { get; set; }

    public long LatencyMs { get; set; }

    public long QueueMs { get; set; }

    public bool Rejected { get; set; }
}

public sealed class LlmCallRow
{
    public int Tick { get; set; }

    public string NpcId { get; set; } = string.Empty;

    public string Variant { get; set; } = string.Empty;

    public string Trigger { get; set; } = string.Empty;

    public int PromptTokens { get; set; }

    public int CacheHit { get; set; }

    public int CacheMiss { get; set; }

    public int CompletionTokens { get; set; }

    public long LatencyMs { get; set; }

    public double CostUsd { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Verdict { get; set; } = string.Empty;

    public string RejectReason { get; set; } = string.Empty;

    public bool ParseError { get; set; }

    public string CacheFile { get; set; } = string.Empty;
}

public sealed class CalibrationRow
{
    public int RequestId { get; set; }

    public int LocalTokens { get; set; }

    public int ApiPromptTokens { get; set; }

    public double Ratio { get; set; }
}

/// <summary>S7: подкуп очевидца и подстава — итог одного прогона.</summary>
public sealed class S7Row
{
    public string Scenario { get; set; } = string.Empty;

    public string Variant { get; set; } = string.Empty;

    public int Seed { get; set; }

    /// <summary>Доля NPC, знающих о краже в конце прогона.</summary>
    public double FinalCoverage { get; set; }

    public int? T50 { get; set; }

    /// <summary>Сколько раз слух не передан из-за обещания молчать.</summary>
    public int SecretBlocked { get; set; }

    /// <summary>Сколько раз NPC проболтался вопреки обещанию.</summary>
    public int SecretLeaked { get; set; }

    /// <summary>Сколько раз NPC сознательно раскрыл секрет действием.</summary>
    public int PromiseBreaks { get; set; }

    /// <summary>Сколько предложений раскрыть секрет отклонил валидатор.</summary>
    public int PromiseRejected { get; set; }

    public int ItemsFound { get; set; }

    public string Arrested { get; set; } = string.Empty;

    public string TrueCulprit { get; set; } = string.Empty;

    public string Framed { get; set; } = string.Empty;

    /// <summary>Арестован подставленный (невиновный).</summary>
    public bool WrongArrest { get; set; }

    /// <summary>Арестован настоящий вор.</summary>
    public bool RightArrest { get; set; }

    /// <summary>Сколько решений (арест, штраф, удар, обещание) валидатор отклонил как «немає підстав».</summary>
    public int ArrestsRejected { get; set; }
}
