using Svidok.Core.Llm;

namespace Svidok.Llm;

/// <summary>Mock-клиент без сети: ответ по правилам V0 в формате JSON модели и задержка 300 мс.</summary>
public sealed class MockLlmClient : ILlmClient
{
    private readonly int _delayMs;
    private int _counter;

    public MockLlmClient(int delayMs = 300)
    {
        _delayMs = delayMs;
    }

    /// <summary>Число фактических обращений к «сети» — используется в тестах кэша.</summary>
    public int NetworkCalls => Volatile.Read(ref _counter);

    public async Task<LlmResult> CompleteAsync(LlmRequest request, CancellationToken ct)
    {
        Interlocked.Increment(ref _counter);
        await Task.Delay(_delayMs, ct).ConfigureAwait(false);

        var content = "{\"action\":\"say\",\"target\":null,\"zone\":null,\"item\":null,\"eventRef\":null,\"questId\":null,"
                      + "\"say\":\"Добрий день.\",\"reason\":\"mock-відповідь\"}";
        var promptTokens = Math.Max(1, (request.System.Length + request.User.Length) / 4);
        return new LlmResult(content, promptTokens, 0, promptTokens, 20, _delayMs, false, null);
    }
}
