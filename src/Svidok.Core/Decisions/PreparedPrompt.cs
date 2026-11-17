namespace Svidok.Core.Decisions;

/// <summary>Готовый промпт, собранный на потоке симуляции (чтобы не читать мир из другого потока).</summary>
public sealed record PreparedPrompt(string System, string User, int LocalTokens);

/// <summary>Решатель, который умеет собрать промпт синхронно и отправить его асинхронно.</summary>
public interface IPreparedDecisionMaker : IDecisionMaker
{
    PreparedPrompt Prepare(DecisionContext ctx);

    Task<DecisionResult> DecidePreparedAsync(PreparedPrompt prompt, DecisionContext ctx, CancellationToken ct);
}
