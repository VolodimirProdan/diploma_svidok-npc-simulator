namespace Svidok.Core.Decisions;

/// <summary>Один вызов модели для вкладки «Нейромережа» и окна диалога (полный промпт и ответ).</summary>
public sealed record DecisionTrace(
    int Tick,
    string NpcId,
    string NpcName,
    string Trigger,
    string Action,
    string Verdict,
    string RejectReason,
    bool ParseError,
    string? Prompt,
    string? RawResponse,
    int PromptTokens,
    int CompletionTokens,
    long LatencyMs,
    double CostUsd,
    bool FromCache);
