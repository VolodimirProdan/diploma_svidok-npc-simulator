using Svidok.Core.Entities;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Decisions;

public sealed record DecisionContext(
    World World,
    Npc Npc,
    Trigger Trigger,
    string? PlayerText,
    IReadOnlyList<DialogLine> History)
{
    /// <summary>Текст ситуации для промпта («Ти щойно дізнався про подію #41.»).</summary>
    public string Situation { get; init; } = string.Empty;

    public int? FocusEventId { get; init; }

    public string? Question { get; init; }

    /// <summary>Живой разговор с игроком в игре: память о нём, доверие, манера речи. В пакетных прогонах null.</summary>
    public InteractiveDialog? Interactive { get; init; }
}

/// <summary>
/// Что NPC знает об игроке в интерактивной игре. Добавляется в промпт только в окне игры,
/// пакетные эксперименты S1–S5 эти данные не получают.
/// </summary>
public sealed record InteractiveDialog(
    string PlayerName,
    string Memory,
    double Trust,
    string? Style,
    IReadOnlyList<string> Gossip,
    IReadOnlyList<string>? Wares = null,
    string? PendingDeal = null);

public interface IDecisionMaker
{
    Variant Variant { get; }

    Task<DecisionResult> DecideAsync(DecisionContext ctx, CancellationToken ct);
}
