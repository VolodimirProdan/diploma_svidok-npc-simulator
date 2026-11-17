using Svidok.Core.Llm;

namespace Svidok.Llm;

/// <summary>Клиент для --dry-run: считает токены и число запросов, но не ходит в сеть.</summary>
public sealed class DryRunLlmClient : ILlmClient
{
    private readonly TokenCounter _counter;
    private int _calls;
    private int _promptTokens;
    private int _completionTokens;

    public DryRunLlmClient(TokenCounter counter)
    {
        _counter = counter;
    }

    public int Calls => Volatile.Read(ref _calls);

    public int PromptTokens => Volatile.Read(ref _promptTokens);

    public int CompletionTokens => Volatile.Read(ref _completionTokens);

    public Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _calls);
        var local = _counter.Count(request.System) + _counter.Count(request.User);
        var prompt = Math.Max(1, (int)Math.Round(local * _counter.Calibration));
        var completion = 40;
        Interlocked.Add(ref _promptTokens, prompt);
        Interlocked.Add(ref _completionTokens, completion);

        var content = "{\"action\":\"ignore\",\"target\":null,\"zone\":null,\"item\":null,\"eventRef\":null,"
                      + "\"questId\":null,\"say\":\"\",\"reason\":\"dry-run\"}";
        return Task.FromResult(new LlmResult(content, prompt, 0, prompt, completion, 0, false, null));
    }
}
