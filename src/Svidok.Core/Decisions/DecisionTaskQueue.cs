using System.Collections.Concurrent;

namespace Svidok.Core.Decisions;

/// <summary>Готовое решение, ожидающее применения к миру.</summary>
public sealed record CompletedDecision(string NpcId, int Tick, DecisionResult Result);

/// <summary>Очередь асинхронных решений модели: запросы выполняются в пуле потоков, готовые складываются в очередь.</summary>
public sealed class DecisionTaskQueue : IDisposable
{
    private readonly ConcurrentQueue<CompletedDecision> _ready = new();
    private readonly List<Task> _inFlight = new();
    private readonly object _gate = new();
    private readonly SemaphoreSlim _limit;
    private int _active;

    public DecisionTaskQueue(int maxConcurrent)
    {
        _limit = new SemaphoreSlim(Math.Max(1, maxConcurrent), Math.Max(1, maxConcurrent));
    }

    public int Active => Volatile.Read(ref _active);

    public int ReadyCount => _ready.Count;

    public int Pending
    {
        get
        {
            lock (_gate)
            {
                return _inFlight.Count(t => !t.IsCompleted);
            }
        }
    }

    public int MaxConcurrent => _limit.CurrentCount + Active;

    public bool TryDequeue(out CompletedDecision result) => _ready.TryDequeue(out result!);

    public void Enqueue(string npcId, int tick, Func<CancellationToken, Task<DecisionResult>> work, CancellationToken ct)
    {
        Interlocked.Increment(ref _active);
        var task = Task.Run(async () =>
        {
            await _limit.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var result = await work(ct).ConfigureAwait(false);
                _ready.Enqueue(new CompletedDecision(npcId, tick, result));
            }
            catch (OperationCanceledException)
            {
                // отмена прогона — результат не нужен
            }
            catch (Exception)
            {
                var fallback = new DecisionResult(
                    LlmDecision.Ignore(reason: "internal error"),
                    LlmDecision.Ignore(reason: "internal error"),
                    ValidationResult.Success,
                    null, 0, true);
                _ready.Enqueue(new CompletedDecision(npcId, tick, fallback));
            }
            finally
            {
                _limit.Release();
                Interlocked.Decrement(ref _active);
            }
        }, ct);

        lock (_gate)
        {
            _inFlight.Add(task);
            if (_inFlight.Count > 512)
            {
                _inFlight.RemoveAll(t => t.IsCompleted);
            }
        }
    }

    public async Task WaitAllAsync()
    {
        Task[] snapshot;
        lock (_gate)
        {
            snapshot = _inFlight.ToArray();
        }
        await Task.WhenAll(snapshot).ConfigureAwait(false);
    }

    public void Dispose() => _limit.Dispose();
}
