namespace Svidok.Core.Llm;

/// <summary>Запрос к модели (модели данных, без HTTP — поэтому живут в Core).</summary>
public sealed record LlmRequest(
    string Model,
    double Temperature,
    int MaxTokens,
    string System,
    string User,
    string? Tag = null);

public sealed record LlmResult(
    string Content,
    int PromptTokens,
    int CacheHitTokens,
    int CacheMissTokens,
    int CompletionTokens,
    long LatencyMs,
    bool FromCache,
    string? Error)
{
    /// <summary>Файл кэша, из которого взят ответ (для llm_calls.csv).</summary>
    public string? CacheFile { get; init; }

    public static LlmResult ErrorResult(string error) =>
        new(string.Empty, 0, 0, 0, 0, 0, false, error);
}

public interface ILlmClient
{
    Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct);
}
