using Svidok.Core.Llm;

namespace Svidok.Core.Decisions;

/// <summary>Ответ модели в разобранном виде (раздел 9.6).</summary>
public sealed record LlmDecision(
    string Action = "ignore",
    string? Target = null,
    string? Zone = null,
    string? Item = null,
    int? EventRef = null,
    string? QuestId = null,
    string Say = "",
    string Reason = "",
    string? MemoryNote = null,
    int? AttitudeDelta = null,
    bool Gossip = false,
    int? Amount = null)
{
    public static LlmDecision Ignore(string say = "", string reason = "") =>
        new("ignore", null, null, null, null, null, say, reason);

    public LlmDecision With(string action, string? say = null) =>
        new(action, Target, Zone, Item, EventRef, QuestId, say ?? Say, Reason, MemoryNote, AttitudeDelta, Gossip, Amount);
}

public enum RejectReason
{
    ActionNotAllowed,
    UnknownEvent,
    LowCredibility,
    NoQuestCompletion,
    InvalidParameter,
    PromisedSecret,
    NoGrounds
}

public static class RejectReasons
{
    public static string NameUk(RejectReason reason)
    {
        return reason switch
        {
            RejectReason.ActionNotAllowed => "дія не дозволена",
            RejectReason.UnknownEvent => "невідома подія",
            RejectReason.LowCredibility => "низька достовірність",
            RejectReason.NoQuestCompletion => "завдання не виконано",
            RejectReason.InvalidParameter => "некоректний параметр",
            RejectReason.PromisedSecret => "обіцяв мовчати",
            RejectReason.NoGrounds => "немає підстав",
            _ => reason.ToString()
        };
    }
}

public sealed record ValidationResult(bool Ok, RejectReason? Reason, string? Details)
{
    public static readonly ValidationResult Success = new(true, null, null);

    public static ValidationResult Fail(RejectReason reason, string? details = null) =>
        new(false, reason, details);
}

public sealed record DecisionResult(
    LlmDecision Proposed,
    LlmDecision Executed,
    ValidationResult Validation,
    LlmResult? Llm,
    int PromptTokensLocal,
    bool ParseError,
    string? PromptText = null,
    string? RawResponse = null);

public sealed record DialogLine(string Speaker, string Text, bool IsPlayer);
