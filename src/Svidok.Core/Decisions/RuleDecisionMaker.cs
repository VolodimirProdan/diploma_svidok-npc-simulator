using Svidok.Core.Entities;
using Svidok.Core.Knowledge;
using Svidok.Core.Quests;
using Svidok.Core.WorldModel;

namespace Svidok.Core.Decisions;

/// <summary>V0 «Правила»: база без нейросети и запасной вариант при ошибке разбора (раздел 9.4).</summary>
public sealed class RuleDecisionMaker : IDecisionMaker
{
    private static readonly string[] SmallTalk =
    [
        "Добрий день.",
        "Мені ніколи, роботи багато.",
        "Чого тобі?",
        "Не заважай, будь ласка.",
        "Поговоримо пізніше."
    ];

    private readonly World _world;
    private readonly ActionCatalog _catalog;
    private readonly Validator _validator;

    public RuleDecisionMaker(World world, ActionCatalog catalog)
    {
        _world = world;
        _catalog = catalog;
        _validator = new Validator(world, catalog);
    }

    public Variant Variant => Variant.V0;

    public Task<DecisionResult> DecideAsync(DecisionContext ctx, CancellationToken ct)
    {
        var decision = Decide(ctx);
        var validation = _validator.Validate(ctx.Npc, decision);
        var executed = decision;
        if (!validation.Ok)
        {
            executed = new LlmDecision("say", Say: "Мені треба в цьому переконатися.", Reason: "fallback");
        }

        var result = new DecisionResult(decision, executed, validation, null, 0, false);
        return Task.FromResult(result);
    }

    public LlmDecision Decide(DecisionContext ctx)
    {
        var npc = ctx.Npc;

        var fire = Best(npc, e => string.Equals(e.Type, "fire", StringComparison.Ordinal) && e.C >= 0.6);
        if (fire is not null)
        {
            var zone = fire.Field("zone");
            if (string.Equals(zone, npc.WorkZone, StringComparison.Ordinal) ||
                string.Equals(zone, npc.HomeZone, StringComparison.Ordinal))
            {
                return new LlmDecision("help_fire", Zone: zone, EventRef: fire.EventId,
                    Say: "Треба гасити!", Reason: "правило V0: пожежа у своїй зоні");
            }
            return new LlmDecision("flee", Zone: npc.HomeZone, EventRef: fire.EventId,
                Say: "Рятуйся!", Reason: "правило V0: пожежа в іншій зоні");
        }

        var crime = Best(npc, e =>
            (string.Equals(e.Type, "theft", StringComparison.Ordinal) || string.Equals(e.Type, "attack", StringComparison.Ordinal))
            && e.C >= 0.9);
        if (crime is not null && npc.Role == Role.Guard)
        {
            var actor = crime.Field("actor");
            if (!string.IsNullOrWhiteSpace(actor) && actor is not ("невідомий" or "хтось") &&
                _world.Npc(actor) is not null &&
                !AlreadyArrestedFor(crime.EventId) &&
                (npc.CurrentAction is null || !string.Equals(npc.CurrentAction.ActionId, "arrest", StringComparison.Ordinal)))
            {
                return new LlmDecision("arrest", Target: actor, EventRef: crime.EventId,
                    Say: "Ти затриманий!", Reason: "правило V0: вартовий бачив злочин");
            }
        }

        var theft = Best(npc, e => string.Equals(e.Type, "theft", StringComparison.Ordinal) && e.C >= 0.5);
        if (theft is not null && npc.Role != Role.Guard)
        {
            return new LlmDecision("report_to_guard", EventRef: theft.EventId,
                Say: "Треба сказати вартовому.", Reason: "правило V0: крадіжка, c ≥ 0.5");
        }

        var weakTheft = Best(npc, e => string.Equals(e.Type, "theft", StringComparison.Ordinal) && e.C >= 0.4);
        if (weakTheft is not null)
        {
            if (npc.Role == Role.Trader)
            {
                return new LlmDecision("close_shop", EventRef: weakTheft.EventId,
                    Say: "Крамницю зачиняю.", Reason: "правило V0: торговець ховає крам");
            }
            return new LlmDecision("hide_valuables", EventRef: weakTheft.EventId,
                Say: "Сховаю цінне.", Reason: "правило V0: крадіжка, c ≥ 0.4");
        }

        if (ctx.Trigger == Trigger.PlayerDialog)
        {
            var quest = _world.Quests.QuestOfGiver(npc.Id);
            if (quest is not null)
            {
                var completed = _world.Log.HasQuestCompleted(quest.Id, Player.PlayerId);
                if (completed && quest.State != QuestState.Rewarded)
                {
                    return new LlmDecision("give_reward", QuestId: quest.Id, Target: Player.PlayerId,
                        Say: "Ось твоя нагорода, " + quest.RewardCoins + " монет.", Reason: "правило V0: завдання виконано");
                }
                return new LlmDecision("refuse", Target: Player.PlayerId,
                    Say: "Спершу принеси скриню.", Reason: "правило V0: завдання не виконано");
            }

            var phrase = SmallTalk[_world.Rng.NextInt(0, SmallTalk.Length, _world.Rng.Sim)];
            return new LlmDecision("say", Say: phrase, Reason: "правило V0: розмова");
        }

        return LlmDecision.Ignore(reason: "правило V0: нічого важливого");
    }

    /// <summary>По одному событию — одно задержание: иначе вартовий арестовывает повторно каждый кулдаун.</summary>
    private bool AlreadyArrestedFor(int? eventId)
    {
        if (eventId is not int id)
        {
            return false;
        }
        var expected = id.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var arrest in _world.Log.OfType("arrest"))
        {
            if (string.Equals(arrest.Get("reason"), expected, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }

    private static KnowledgeEntry? Best(Npc npc, Func<KnowledgeEntry, bool> predicate)
    {
        KnowledgeEntry? best = null;
        foreach (var entry in npc.Knowledge.Entries)
        {
            if (!predicate(entry))
            {
                continue;
            }
            if (best is null || entry.C > best.C || (Math.Abs(entry.C - best.C) < 0.0001 && entry.UpdatedTick > best.UpdatedTick))
            {
                best = entry;
            }
        }
        return best;
    }

    public string CatalogDescription => "Дії: " + _catalog.ActionListForPrompt();
}
